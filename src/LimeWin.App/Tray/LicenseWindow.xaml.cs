using System.IO;
using Wpf.Ui.Controls;

namespace LimeWin.App.Tray;

/// <summary>Shows NOTICE.md (embedded at build time) read-only.</summary>
public partial class LicenseWindow : FluentWindow
{
    public LicenseWindow()
    {
        InitializeComponent();
        using var stream = typeof(LicenseWindow).Assembly.GetManifestResourceStream("NOTICE.md");
        if (stream is null) return;
        using var reader = new StreamReader(stream);
        NoticeText.Text = reader.ReadToEnd();
    }
}
