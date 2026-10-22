using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using LimeWin.App.Input;
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

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstance = new Mutex(initiallyOwned: true, SingleInstanceName, out var createdNew);
        if (!createdNew)
        {
            _singleInstance.Dispose();
            _singleInstance = null;
            Shutdown();
            return;
        }

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

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
        _hookHealth?.Stop();
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        TaskScheduler.UnobservedTaskException -= OnUnobservedTaskException;
        _hooks?.Dispose(); // ends the hook thread
        _wheel?.Close();
        _tray?.Dispose();
        if (_singleInstance is not null)
        {
            _singleInstance.ReleaseMutex();
            _singleInstance.Dispose();
        }
        base.OnExit(e);
    }

    void SetWheelEnabled(bool on)
    {
        if (on)
        {
            try
            {
                _hooks?.Install();
            }
            catch (Win32Exception)
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
        Dispatcher.BeginInvoke(() => StartJobs(item, files));

    static void StartJobs(WheelItem item, IReadOnlyList<string> files)
    {
        if (item.Operation is ConvertOp convert)
        {
            foreach (var file in files)
                Services.Jobs.Run($"{Path.GetFileName(file)} → {item.Label}", ctx => Services.Router.ConvertAsync(file, convert.Target, ctx));
        }
        else
        {
            // Tools and actions arrive in later tasks.
            Services.Jobs.Run(item.Label, _ => throw new LimeException("준비 중입니다."));
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
            _tray?.Notify(UnexpectedError);
        }
        catch (Exception)
        {
            // Recovery must never throw back into the dispatcher.
        }
    }

    void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category is UserPreferenceCategory.General or UserPreferenceCategory.Color)
            Dispatcher.BeginInvoke(() => ApplicationThemeManager.ApplySystemTheme());
    }
}
