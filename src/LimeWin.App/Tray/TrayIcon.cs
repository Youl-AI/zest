using System.Windows.Controls;
using System.Windows.Media;
using H.NotifyIcon;

namespace LimeWin.App.Tray;

/// <summary>Notification-area icon and its menu: wheel on/off, run at sign-in, licenses, quit.</summary>
public sealed class TrayIcon : IDisposable
{
    readonly TaskbarIcon _icon;
    readonly MenuItem _wheel;
    LicenseWindow? _licenses;

    /// <param name="wheelEnabled">Called with the new state when "휠 켜기" is toggled.</param>
    /// <param name="quit">Called for "종료".</param>
    public TrayIcon(bool wheelOn, Action<bool> wheelEnabled, Action quit)
    {
        var wheel = _wheel = new MenuItem { Header = "휠 켜기", IsCheckable = true, IsChecked = wheelOn };
        wheel.Click += (_, _) => wheelEnabled(wheel.IsChecked);

        var login = new MenuItem { Header = "윈도우 시작 시 실행", IsCheckable = true, IsChecked = LoginItem.IsEnabled };
        login.Click += (_, _) => LoginItem.Set(login.IsChecked);

        var licenses = new MenuItem { Header = "라이선스 보기" };
        licenses.Click += (_, _) => ShowLicenses();

        var exit = new MenuItem { Header = "종료" };
        exit.Click += (_, _) => quit();

        var menu = new ContextMenu();
        menu.Items.Add(wheel);
        menu.Items.Add(login);
        menu.Items.Add(licenses);
        menu.Items.Add(new Separator());
        menu.Items.Add(exit);

        _icon = new TaskbarIcon
        {
            ToolTipText = "Lime",
            // Placeholder until the real lime-slice artwork exists (Task 6B).
            IconSource = new GeneratedIconSource
            {
                Text = "L",
                Foreground = Brushes.White,
                Background = new SolidColorBrush(Color.FromRgb(0x6F, 0xA8, 0x3A)),
                FontWeight = System.Windows.FontWeights.Bold,
            },
            ContextMenu = menu,
        };
        // Efficiency Mode would lower the process priority and slow the hooks and conversions, so keep it off.
        _icon.ForceCreate(enablesEfficiencyMode: false);
    }

    /// <summary>Reflects the real wheel state, e.g. after the hooks failed to install.</summary>
    public void SetWheelChecked(bool on) => _wheel.IsChecked = on;

    /// <summary>A short notification balloon from the tray icon.</summary>
    public void Notify(string message) => _icon.ShowNotification("Lime", message);

    void ShowLicenses()
    {
        if (_licenses is null)
        {
            _licenses = new LicenseWindow();
            _licenses.Closed += (_, _) => _licenses = null;
            _licenses.Show();
        }
        _licenses.Activate();
    }

    public void Dispose()
    {
        _licenses?.Close();
        _icon.Dispose();
    }
}
