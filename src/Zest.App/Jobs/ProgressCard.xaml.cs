using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Zest.App.Wheel;

namespace Zest.App.Jobs;

/// <summary>One job's card: title, progress, status line and a cancel button. Finished cards close themselves after 4 s; failed ones on click.</summary>
public partial class ProgressCard : UserControl
{
    static readonly TimeSpan AutoClose = TimeSpan.FromSeconds(4);
    const string DoneGlyph = "";   // Completed (check in a circle)
    const string FailedGlyph = ""; // Error (exclamation in a circle)
    const string CancelledGlyph = ""; // Cancel (x)

    enum State { Running, Done, Failed, Cancelled }

    readonly TranslateTransform _rise = new();
    State _state = State.Running;
    IReadOnlyList<string> _results = [];
    DispatcherTimer? _closeTimer;
    bool _leaving;

    public event Action<ProgressCard>? CloseRequested;

    public ProgressCard(string title, Action cancel)
    {
        InitializeComponent();
        RenderTransform = _rise;
        TitleText.Text = title;
        TitleText.ToolTip = title;
        _cancel = cancel;
        CancelButton.Click += (_, _) => RequestCancel();
        MouseLeftButtonUp += OnClick;
    }

    const string RunningText = "변환 중…"; // the XAML's initial status
    const string WaitingText = "대기 중";

    readonly Action _cancel;
    bool _cancelling;

    /// <summary>Whether the job is still running (not done, failed or cancelled).</summary>
    public bool IsRunning => _state == State.Running;

    /// <summary>The cancel button's action; does nothing once the job has ended or is already cancelling.</summary>
    public void RequestCancel()
    {
        if (_state != State.Running || _cancelling) return;
        _cancelling = true;
        CancelButton.IsEnabled = false;
        StatusText.Text = "취소하는 중…";
        _cancel();
    }

    /// <summary>"대기 중" while the job waits for its engine (other jobs are using it), then back to the running text.</summary>
    public void SetWaiting(bool waiting)
    {
        if (_state != State.Running || _cancelling) return;
        StatusText.Text = waiting ? WaitingText : RunningText;
    }

    /// <summary>Entrance: 8 px up and fade in over 200 ms (fade only when animations are off in Windows).</summary>
    public void AnimateIn()
    {
        Opacity = 0;
        Motion.To(this, OpacityProperty, 1.0, WheelStyle.CardEnter, WheelStyle.EaseOut);
        if (Motion.ReduceMotion) return;
        _rise.Y = WheelStyle.CardRise;
        Motion.To(_rise, TranslateTransform.YProperty, 0.0, WheelStyle.CardEnter, WheelStyle.EaseOut);
    }

    /// <summary>Exit: fade out (and sink back the way it came) over 150 ms, then <paramref name="done"/>.</summary>
    public void AnimateOut(Action done)
    {
        if (_leaving) return;
        _leaving = true;
        IsHitTestVisible = false;
        Motion.To(this, OpacityProperty, 0.0, WheelStyle.CardExit, WheelStyle.EaseOut, done);
        if (!Motion.ReduceMotion) Motion.To(_rise, TranslateTransform.YProperty, WheelStyle.CardRise, WheelStyle.CardExit, WheelStyle.EaseOut);
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
        Finish(State.Done, results.Count > 0 ? "완료 — 클릭하면 결과를 보여 줍니다" : "완료", DoneGlyph, "SystemFillColorSuccessBrush");
        Bar.Visibility = Visibility.Collapsed;
        StartAutoClose();
    }

    public void ShowFailed(string message)
    {
        Finish(State.Failed, message, FailedGlyph, "SystemFillColorCriticalBrush");
        Bar.Visibility = Visibility.Collapsed;
    }

    public void ShowCancelled()
    {
        Finish(State.Cancelled, "취소됨", CancelledGlyph, "TextFillColorSecondaryBrush");
        Bar.Visibility = Visibility.Collapsed;
        StartAutoClose();
    }

    void Finish(State state, string status, string glyph, string iconBrushKey)
    {
        _state = state;
        Bar.IsIndeterminate = false;
        StatusText.Text = status;
        StateIcon.Text = glyph;
        StateIcon.SetResourceReference(TextBlock.ForegroundProperty, iconBrushKey);
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
