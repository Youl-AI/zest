using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace LimeWin.App.Jobs;

/// <summary>One job's card: title, progress, status line and a cancel button. Finished cards close themselves after 4 s; failed ones on click.</summary>
public partial class ProgressCard : UserControl
{
    static readonly TimeSpan AutoClose = TimeSpan.FromSeconds(4);

    enum State { Running, Done, Failed, Cancelled }

    State _state = State.Running;
    IReadOnlyList<string> _results = [];
    DispatcherTimer? _closeTimer;

    public event Action<ProgressCard>? CloseRequested;

    public ProgressCard(string title, Action cancel)
    {
        InitializeComponent();
        TitleText.Text = title;
        TitleText.ToolTip = title;
        CancelButton.Click += (_, _) =>
        {
            CancelButton.IsEnabled = false;
            StatusText.Text = "취소하는 중…";
            cancel();
        };
        MouseLeftButtonUp += OnClick;
    }

    public void SetProgress(double fraction)
    {
        if (_state != State.Running) return;
        Bar.IsIndeterminate = false;
        Bar.Value = fraction;
    }

    public void ShowDone(IReadOnlyList<string> results)
    {
        _results = results;
        Finish(State.Done, results.Count > 0 ? "완료 — 클릭하면 결과를 보여 줍니다" : "완료");
        Bar.Value = 1;
        StartAutoClose();
    }

    public void ShowFailed(string message)
    {
        Finish(State.Failed, message);
        Bar.Visibility = Visibility.Collapsed;
    }

    public void ShowCancelled()
    {
        Finish(State.Cancelled, "취소됨");
        Bar.Visibility = Visibility.Collapsed;
        StartAutoClose();
    }

    void Finish(State state, string status)
    {
        _state = state;
        Bar.IsIndeterminate = false;
        StatusText.Text = status;
        CancelButton.Visibility = Visibility.Collapsed;
    }

    void StartAutoClose()
    {
        _closeTimer = new DispatcherTimer { Interval = AutoClose };
        _closeTimer.Tick += (_, _) => RequestClose();
        _closeTimer.Start();
    }

    void OnClick(object sender, MouseButtonEventArgs e)
    {
        switch (_state)
        {
            case State.Running:
                return;
            case State.Done when _results.Count > 0:
                try
                {
                    Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{_results[0]}\"") { UseShellExecute = false })?.Dispose();
                }
                catch (Win32Exception) { /* Explorer could not start; nothing else to offer */ }
                break;
        }
        RequestClose();
    }

    void RequestClose()
    {
        _closeTimer?.Stop();
        CloseRequested?.Invoke(this);
    }
}
