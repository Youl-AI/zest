using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using Zest.App.Actions;
using Zest.App.Input;
using Zest.App.Jobs;
using Zest.App.Render;
using Zest.App.Theme;
using Zest.App.Tools;
using Zest.App.Tray;
using Zest.App.Wheel;
using Zest.Core.Catalog;
using Zest.Core.Jobs;
using Zest.Core.Migration;
using Zest.Core.Office;
using Microsoft.Win32;
using Wpf.Ui.Appearance;

namespace Zest.App;

public partial class App : Application
{
    const string SingleInstanceName = @"Local\Zest.SingleInstance";
    internal const string LegacyInstanceName = @"Local\LimeWin.SingleInstance";
    const string HooksFailed = "키 감지를 켜지 못했습니다.";
    const string UnexpectedError = "예기치 않은 오류가 발생했지만 Zest는 계속 실행됩니다.";
    static readonly TimeSpan HookHealthInterval = TimeSpan.FromSeconds(5);

    Mutex? _singleInstance;
    LowLevelHooks? _hooks;
    WheelController? _wheel;
    TrayIcon? _tray;
    DispatcherTimer? _hookHealth;
    bool _wheelEnabled;
    long? _lastErrorBalloon; // Environment.TickCount64 of the last error balloon
    const long ErrorBalloonIntervalMs = 30_000;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (e.Args is ["--render", var folder, ..])
        {
            // Review renders only: no mutex, tray, hooks or windows.
            Shutdown(RenderHarness.Run(folder));
            return;
        }

        if (e.Args is ["--render-readme", var readmeFolder, ..])
        {
            // README images only: no mutex, tray, hooks, windows or migration.
            Shutdown(RenderHarness.RunReadme(readmeFolder));
            return;
        }

        if (Mutex.TryOpenExisting(LegacyInstanceName, out var legacy))
        {
            legacy.Dispose();
            MessageBox.Show("이전 버전인 Lime이 실행 중입니다. 트레이에서 Lime을 종료한 뒤 Zest를 다시 실행하세요.", "Zest", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        _singleInstance = new Mutex(initiallyOwned: true, SingleInstanceName, out var createdNew);
        if (!createdNew)
        {
            _singleInstance.Dispose();
            _singleInstance = null;
            Shutdown();
            return;
        }

        try
        {
            Start();
        }
        catch (Exception ex)
        {
            // Without this the process would linger invisibly, holding the mutex so every relaunch exits silently.
            MessageBox.Show($"Zest를 시작하지 못했습니다: {ex.Message}", "Zest", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(); // OnExit cleans up what was built and releases the mutex
            return;
        }

        // Last-resort handlers only once the app is fully up; a failed startup must not be swallowed.
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    void Start()
    {
        LegacyMigration.MoveDataFolder(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
        LoginItem.MigrateLegacy();
        ApplicationThemeManager.ApplySystemTheme();
        ZestTheme.Apply();
        SystemEvents.UserPreferenceChanged +=OnUserPreferenceChanged;

        Services.Start();
        // Work folders a crashed or killed run left in %TEMP%\Zest; best effort, off the UI thread.
        _ = Task.Run(() =>
        {
            try { OfficeWorkFolder.SweepStale(); }
            catch (Exception) { /* nothing here may disturb startup */ }
        });

        var wheel = _wheel = new WheelController(Services.Capabilities);
        wheel.ItemDropped += OnItemDropped;

        _hooks = new LowLevelHooks(Dispatcher);
        var session = new DragSession(_hooks);
        session.Tapped += (mode, at) =>
        {
            if (wheel.IsOpen) wheel.SwitchMode(mode);
            else wheel.Show(mode, at);
        };
        session.ButtonReleased += wheel.OnButtonReleased;
        session.Escape += wheel.OnEscape;

        _tray = new TrayIcon(wheelOn: true, SetWheelEnabled, Shutdown);
        SetWheelEnabled(true);

        // Windows silently removes a low-level hook that times out; check every 5 s and reinstall if input passes us by.
        _hookHealth = new DispatcherTimer { Interval = HookHealthInterval };
        _hookHealth.Tick += (_, _) => CheckHooks();
        _hookHealth.Start();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            try
            {
                _hookHealth?.Stop();
                SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
                TaskScheduler.UnobservedTaskException -= OnUnobservedTaskException;
                _hooks?.Dispose(); // ends the hook thread
                _wheel?.Close();
                _tray?.Dispose();
            }
            finally
            {
                // Released on every exit path, even if cleanup above throws, and before the waits below, so a new Zest
                // can start while this one is still finishing its jobs and releasing Office.
                if (_singleInstance is not null)
                {
                    _singleInstance.ReleaseMutex();
                    _singleInstance.Dispose();
                    _singleInstance = null;
                }
            }
            Services.Stop(); // cancels running jobs (up to 3 s for their clean-up), then releases Office
        }
        finally
        {
            base.OnExit(e);
        }
    }

    void SetWheelEnabled(bool on)
    {
        if (on)
        {
            try
            {
                _hooks?.Install();
            }
            catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
            {
                on = false;
                _tray?.Notify(HooksFailed);
            }
        }
        else
        {
            _hooks?.Uninstall();
            _wheel?.Close();
        }
        _wheelEnabled = on;
        _tray?.SetWheelChecked(on);
    }

    void CheckHooks()
    {
        if (!_wheelEnabled || _hooks is null || !_hooks.LooksDead()) return;
        _wheel?.Close();
        _hooks.Uninstall();
        SetWheelEnabled(true);
    }

    void OnItemDropped(WheelItem item, IReadOnlyList<string> files, Point at) =>
        // Start the jobs after OLE's Drop callback has returned, so the drag source is released immediately.
        Dispatcher.BeginInvoke(() => StartJobs(item, files, at));

    static void StartJobs(WheelItem item, IReadOnlyList<string> files, Point at)
    {
        switch (item.Operation)
        {
            case ConvertOp convert:
                // One card per file ("PNG 변환" over "사진.jpg"), so each shows and cancels on its own.
                foreach (var file in files)
                    Services.Jobs.Run($"{item.Label} 변환", JobDetail.Of(file), JobKind.Convert, ctx => Services.Router.ConvertAsync(file, convert.Target, ctx));
                break;
            case ToolOp tool:
                ToolDispatcher.Handle(tool.Tool, files, at);
                break;
            case ActionOp action:
                ActionRunner.Run(action.Action, files, at);
                break;
        }
    }

    // Last-resort handlers: get the overlay out of the user's way, say so, keep running. Nothing is logged (and no input ever is).
    void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        e.Handled = true;
        RecoverFromError();
    }

    void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        e.SetObserved();
        Dispatcher.BeginInvoke(RecoverFromError);
    }

    void RecoverFromError()
    {
        try
        {
            _wheel?.Close();
            // At most one balloon per 30 s, so an error recurring in a timer tick cannot spam the user.
            var now = Environment.TickCount64;
            if (_lastErrorBalloon is null || now - _lastErrorBalloon.Value >= ErrorBalloonIntervalMs)
            {
                _lastErrorBalloon = now;
                _tray?.Notify(UnexpectedError);
            }
        }
        catch (Exception)
        {
            // Recovery must never throw back into the dispatcher.
        }
    }

    void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category is UserPreferenceCategory.General or UserPreferenceCategory.Color)
        {
            Dispatcher.BeginInvoke(() =>
            {
                ApplicationThemeManager.ApplySystemTheme();
                ZestTheme.Apply();
                _tray?.RefreshIcon(); // the taskbar may have switched between light and dark
            });
        }
    }
}
