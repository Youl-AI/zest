using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using LimeWin.App.Input;
using LimeWin.App.Render;
using LimeWin.App.Tools;
using LimeWin.App.Tray;
using LimeWin.App.Wheel;
using LimeWin.Core;
using LimeWin.Core.Catalog;
using Microsoft.Win32;
using Wpf.Ui.Appearance;

namespace LimeWin.App;

public partial class App : Application
{
    const string SingleInstanceName = @"Local\LimeWin.SingleInstance";
    const string HooksFailed = "키 감지를 켜지 못했습니다.";
    const string UnexpectedError = "예기치 않은 오류가 발생했지만 Lime은 계속 실행됩니다.";
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
            MessageBox.Show($"Lime을 시작하지 못했습니다: {ex.Message}", "Lime", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(); // OnExit cleans up what was built and releases the mutex
            return;
        }

        // Last-resort handlers only once the app is fully up; a failed startup must not be swallowed.
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    void Start()
    {
        ApplicationThemeManager.ApplySystemTheme();
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;

        Services.Start();

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
            _hookHealth?.Stop();
            SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
            TaskScheduler.UnobservedTaskException -= OnUnobservedTaskException;
            _hooks?.Dispose(); // ends the hook thread
            _wheel?.Close();
            _tray?.Dispose();
            Services.Stop();
        }
        finally
        {
            // Released on every exit path, even if cleanup above throws.
            if (_singleInstance is not null)
            {
                _singleInstance.ReleaseMutex();
                _singleInstance.Dispose();
                _singleInstance = null;
            }
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
                foreach (var file in files)
                    Services.Jobs.Run($"{Path.GetFileName(file)} → {item.Label}", ctx => Services.Router.ConvertAsync(file, convert.Target, ctx));
                break;
            case ToolOp tool:
                ToolDispatcher.Handle(tool.Tool, files, at);
                break;
            default:
                // Actions arrive in a later task.
                Services.Jobs.Run(item.Label, _ => throw new LimeException("준비 중입니다."));
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
                _tray?.RefreshIcon(); // the taskbar may have switched between light and dark
            });
        }
    }
}
