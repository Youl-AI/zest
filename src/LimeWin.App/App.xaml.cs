using System.IO;
using System.Windows;
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

    Mutex? _singleInstance;
    LowLevelHooks? _hooks;
    WheelController? _wheel;
    TrayIcon? _tray;

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
        session.Escape += wheel.Close;
        _hooks.Install();

        _tray = new TrayIcon(wheelOn: true, SetWheelEnabled, Shutdown);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        _hooks?.Dispose();
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
            _hooks?.Install();
        }
        else
        {
            _hooks?.Uninstall();
            _wheel?.Close();
        }
    }

    static void OnItemDropped(WheelItem item, IReadOnlyList<string> files, Point at)
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

    void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category is UserPreferenceCategory.General or UserPreferenceCategory.Color)
            Dispatcher.BeginInvoke(() => ApplicationThemeManager.ApplySystemTheme());
    }
}
