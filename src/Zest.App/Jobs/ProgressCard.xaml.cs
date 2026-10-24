using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Zest.App.Theme;
using Zest.App.Wheel;
using Zest.Core.Design;
using Zest.Core.Jobs;

namespace Zest.App.Jobs;

/// <summary>
/// One job's card: the task as the title ("PNG 변환", "압축"), the file as the detail line, a percent and 취소 while it runs,
/// and a 3 px bar in the citrus of the wheel the job came from. Finished cards close themselves after 4 s; failed ones on click.
/// </summary>
public partial class ProgressCard : UserControl
{
    /// <summary>The 344 px card plus its shadow room on both sides.</summary>
    public const double OuterWidth = SurfaceWidth + 2 * SideRoom;
    public const double SurfaceWidth = 344, SideRoom = 14;
    /// <summary>Shadow room inside the transparent host window; the bottom 10 is also the gap between stacked cards.</summary>
    public static readonly Thickness ShadowRoom = new(SideRoom, 0, SideRoom, 10);

    static readonly TimeSpan AutoClose = TimeSpan.FromSeconds(4);
    static readonly Duration Sweep = TimeSpan.FromMilliseconds(1200);
    const double SweepFraction = 0.3;
    const string ClockGlyph = ""; // Recent (clock)
    const string ExplorerHint = "클릭하면 탐색기에서 보기";

    enum State { Running, Done, Failed, Cancelled }

    readonly TranslateTransform _rise = new();
    readonly TranslateTransform _sweep = new();
    readonly string _detail;
    readonly Action _cancel;
    State _state = State.Running;
    IReadOnlyList<string> _results = [];
    DispatcherTimer? _closeTimer;
    double? _fraction; // null = indeterminate
    bool _waiting, _cancelling, _leaving;

    public event Action<ProgressCard>? CloseRequested;

    public ProgressCard(string title, string detail, JobKind kind, Action cancel)
    {
        InitializeComponent();
        RenderTransform = _rise;
        _detail = detail;
        _cancel = cancel;
        TitleText.Text = title;
        TitleText.ToolTip = title;
        SetDetail(detail);

        var citrus = ZestColors.For(JobKinds.ModeOf(kind)).Name;
        BarFill.SetResourceReference(Border.BackgroundProperty, $"Zest.{citrus}.Ink");
        DoneBadge.SetResourceReference(Border.BackgroundProperty, $"Zest.{citrus}.Fill");
        DoneGlyph.SetResourceReference(TextBlock.ForegroundProperty, $"Zest.{citrus}.OnFill");
        CardShadow.Attach(Shadow);

        BarFill.RenderTransform = _sweep;
        Bar.SizeChanged += (_, _) => UpdateBar();
        CancelButton.Click += (_, _) => RequestCancel();
        MouseLeftButtonUp += OnClick;
        Unloaded += (_, _) => StopSweep();
        Loaded += (_, _) => UpdateBar();
    }

    /// <summary>Whether the job is still running (not done, failed or cancelled).</summary>
    public bool IsRunning => _state == State.Running;

    /// <summary>The cancel button's action; does nothing once the job has ended or is already cancelling.</summary>
    public void RequestCancel()
    {
        if (_state != State.Running || _cancelling) return;
        _cancelling = true;
        CancelButton.IsEnabled = false;
        SetDetail("취소하는 중…");
        _cancel();
    }

    /// <summary>"{detail} · 대기 중" with a clock while the job waits for its engine (other jobs are using it), then back.</summary>
    public void SetWaiting(bool waiting)
    {
        if (_state != State.Running || _cancelling) return;
        _waiting = waiting;
        SetDetail(waiting ? $"{_detail} · 대기 중" : _detail);
        ShowLeadBadge(waiting ? ClockGlyph : null, "Zest.Seg", "Zest.Text2");
        Bar.Visibility = waiting ? Visibility.Collapsed : Visibility.Visible;
        UpdatePercent();
        UpdateBar();
    }

    public void SetProgress(double fraction)
    {
        if (_state != State.Running) return;
        _fraction = Math.Clamp(fraction, 0, 1);
        UpdatePercent();
        UpdateBar();
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

    /// <summary>A check in the job's citrus, the bar full, "{result} · 클릭하면 탐색기에서 보기".</summary>
    public void ShowDone(IReadOnlyList<string> results)
    {
        _results = results;
        Finish(State.Done, results.Count switch
        {
            0 => "완료",
            1 => $"{Path.GetFileName(results[0])} · {ExplorerHint}",
            _ => $"{Path.GetFileName(results[0])} 외 {results.Count - 1}개 · {ExplorerHint}",
        });
        DoneBadge.Visibility = Visibility.Visible;
        _fraction = 1;
        Bar.Visibility = Visibility.Visible;
        UpdateBar();
        StartAutoClose();
    }

    /// <summary>"!" on the danger wash; the title stays and the message (up to three lines) replaces the detail.</summary>
    public void ShowFailed(string message)
    {
        Finish(State.Failed, message);
        ShowLeadBadge("!", "Zest.DangerWash", "Zest.Danger");
        DetailText.TextWrapping = TextWrapping.Wrap;
        DetailText.MaxHeight = 3 * DetailText.LineHeight;
        Bar.Visibility = Visibility.Collapsed;
    }

    public void ShowCancelled()
    {
        Finish(State.Cancelled, "취소됨");
        Bar.Visibility = Visibility.Collapsed;
        StartAutoClose();
    }

    void Finish(State state, string detail)
    {
        _state = state;
        _waiting = false;
        StopSweep();
        SetDetail(detail);
        ShowLeadBadge(null, "Zest.Seg", "Zest.Text2");
        CancelButton.Visibility = Visibility.Collapsed;
        PercentText.Visibility = Visibility.Collapsed;
    }

    void SetDetail(string text)
    {
        DetailText.Text = text;
        DetailText.ToolTip = text;
    }

    /// <summary>The left badge: a glyph (Fluent icon, or "!" set in the UI font) on a background, or hidden for null.</summary>
    void ShowLeadBadge(string? glyph, string backgroundKey, string foregroundKey)
    {
        LeadBadge.Visibility = glyph is null ? Visibility.Collapsed : Visibility.Visible;
        if (glyph is null) return;
        LeadGlyph.Text = glyph;
        var icon = glyph.Length == 1 && glyph[0] >= '';
        LeadGlyph.FontFamily = icon ? WheelStyle.IconFont : WheelStyle.ChipFont;
        LeadGlyph.FontWeight = icon ? FontWeights.Normal : FontWeights.Bold;
        LeadGlyph.FontSize = icon ? 12 : 13;
        LeadBadge.SetResourceReference(Border.BackgroundProperty, backgroundKey);
        LeadGlyph.SetResourceReference(TextBlock.ForegroundProperty, foregroundKey);
    }

    void UpdatePercent()
    {
        var show = _state == State.Running && !_waiting && _fraction is not null;
        PercentText.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (show) PercentText.Text = ((int)Math.Round(_fraction!.Value * 100)).ToString(CultureInfo.InvariantCulture) + "%";
    }

    /// <summary>Determinate: the fill covers the fraction. Indeterminate: a 30 % segment sweeps across (nothing with animations off).</summary>
    void UpdateBar()
    {
        var width = Bar.ActualWidth;
        if (width <= 0) return;
        if (_fraction is { } f)
        {
            StopSweep();
            BarFill.Width = width * f;
            return;
        }
        if (_state != State.Running || _waiting || Motion.ReduceMotion || !IsLoaded)
        {
            StopSweep();
            BarFill.Width = 0;
            return;
        }
        BarFill.Width = width * SweepFraction;
        var sweep = new DoubleAnimation(-BarFill.Width, width, Sweep) { RepeatBehavior = RepeatBehavior.Forever };
        _sweep.BeginAnimation(TranslateTransform.XProperty, sweep);
    }

    void StopSweep()
    {
        _sweep.BeginAnimation(TranslateTransform.XProperty, null);
        _sweep.X = 0;
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
