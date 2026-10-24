using System.Collections;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shell;
using Zest.App.Wheel;

namespace Zest.App.Theme;

/// <summary>
/// The borderless Zest popup frame (template <c>Zest.WindowTemplate</c> in <c>ZestControls.xaml</c>): a 14 px rounded
/// surface with a soft shadow inside a transparent <see cref="ShadowMargin"/>, a centred <see cref="Heading"/> with a round
/// close button, the window's content, and an optional <see cref="Footer"/> bar under a hairline.
/// <para>
/// The window's <c>Width</c>/<c>Height</c> include the shadow margin on every side (a 400 px frame is <c>Width = 448</c>), so
/// the HWND stays symmetric around the visible frame and centring it centres the frame. Placement is left to the caller
/// (<c>ToolDispatcher.Present</c>); this class only sizes itself when <c>SizeToContent</c> asks for it.
/// </para>
/// </summary>
public class ZestWindow : Window
{
    /// <summary>Transparent band around the frame that holds the shadow (and, when resizable, the 8 px grip).</summary>
    public const double ShadowMargin = 24;

    const double LightShadowOpacity = 0.14, DarkShadowOpacity = 0.45;
    static readonly TimeSpan Enter = WheelStyle.CardEnter, Exit = WheelStyle.CardExit;
    const double EnterScale = 0.96;

    public static readonly DependencyProperty HeadingProperty = DependencyProperty.Register(
        nameof(Heading), typeof(string), typeof(ZestWindow), new PropertyMetadata("", OnHeadingChanged));

    public static readonly DependencyProperty FooterProperty = DependencyProperty.Register(
        nameof(Footer), typeof(object), typeof(ZestWindow), new PropertyMetadata(null, OnFooterChanged));

    public static readonly DependencyProperty IsResizableProperty = DependencyProperty.Register(
        nameof(IsResizable), typeof(bool), typeof(ZestWindow), new PropertyMetadata(false, OnIsResizableChanged));

    /// <summary>Follows the <c>Zest.Window</c> brush so a live light/dark switch re-picks the shadow strength.</summary>
    static readonly DependencyProperty SurfaceProperty = DependencyProperty.Register(
        "Surface", typeof(Brush), typeof(ZestWindow), new PropertyMetadata(null, (d, _) => ((ZestWindow)d).UpdateShadow()));

    /// <summary>The frame's open/close scale; set from code because a transform written in the template is frozen.</summary>
    readonly ScaleTransform _scale = new();
    FrameworkElement? _frame;
    Border? _shadow;
    ButtonBase? _close;
    FrameworkElement? _header;
    bool _exiting, _exited;

    public ZestWindow()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = true;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.Manual;
        MinWidth = 0;
        MinHeight = 0;
        UseLayoutRounding = true;
        SetResourceReference(TemplateProperty, "Zest.WindowTemplate");
        SetResourceReference(ForegroundProperty, "Zest.Text");
        SetResourceReference(SurfaceProperty, "Zest.Window");
    }

    /// <summary>Title shown centred in the header (Bold 14, one line, ellipsis); also the taskbar title unless <c>Title</c> is set.</summary>
    public string Heading
    {
        get => (string)GetValue(HeadingProperty);
        set => SetValue(HeadingProperty, value);
    }

    /// <summary>
    /// The bottom bar's content (usually a <c>DockPanel</c> with <c>Zest.FooterBar</c>); null hides the bar and its hairline.
    /// </summary>
    public object? Footer
    {
        get => GetValue(FooterProperty);
        set => SetValue(FooterProperty, value);
    }

    /// <summary>
    /// False (default): fixed size, <c>ResizeMode.NoResize</c>. True: the 8 px band just outside the frame resizes the
    /// window (a <c>WindowChrome</c> with no caption and no glass).
    /// </summary>
    public bool IsResizable
    {
        get => (bool)GetValue(IsResizableProperty);
        set => SetValue(IsResizableProperty, value);
    }

    /// <summary>Plays the open and close motion (default). The render harness turns it off to draw the settled frame.</summary>
    public bool Animate { get; set; } = true;

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        if (_close is not null) _close.Click -= OnCloseClick;
        if (_header is not null) _header.MouseLeftButtonDown -= OnHeaderMouseDown;

        _frame = GetTemplateChild("PART_Frame") as FrameworkElement;
        _shadow = GetTemplateChild("PART_Shadow") as Border;
        _close = GetTemplateChild("PART_Close") as ButtonBase;
        _header = GetTemplateChild("PART_Header") as FrameworkElement;

        if (_frame is not null) _frame.RenderTransform = _scale;
        if (_close is not null) _close.Click += OnCloseClick;
        if (_header is not null) _header.MouseLeftButtonDown += OnHeaderMouseDown;
        UpdateShadow();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        ApplyTemplate();
        FitToContent();
        if (Animate) PlayEnter();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (_exited) return; // the exit motion has run: Closing was already raised once, close for real
        if (_exiting)
        {
            e.Cancel = true; // a second Close (Esc twice) while fading: let the running exit finish it
            return;
        }
        base.OnClosing(e);
        if (e.Cancel || !Animate || !IsVisible || _frame is null) return;

        e.Cancel = true;
        _exiting = true;
        IsHitTestVisible = false; // no second click on a button that is fading out (keys: OnPreviewKeyDown)
        var curve = WheelStyle.EaseOut;
        Motion.To(_frame, OpacityProperty, 0, Exit, curve, () =>
        {
            _exited = true;
            Close();
        });
        if (!Motion.ReduceMotion)
        {
            Motion.To(_scale, ScaleTransform.ScaleXProperty, EnterScale, Exit, curve);
            Motion.To(_scale, ScaleTransform.ScaleYProperty, EnterScale, Exit, curve);
        }
    }

    /// <summary>While the exit motion runs, no key reaches the window: a second Enter must not run the action again.</summary>
    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (_exiting) e.Handled = true;
        else base.OnPreviewKeyDown(e);
    }

    protected override IEnumerator LogicalChildren
    {
        get
        {
            var children = base.LogicalChildren;
            while (children is not null && children.MoveNext()) yield return children.Current;
            if (Footer is not null) yield return Footer;
        }
    }

    /// <summary>
    /// <c>SizeToContent</c> is applied only during <c>Show</c>, so a window centred before it is shown would be centred at
    /// its pre-show size. The handle is created here (by the caller's first <c>WindowInteropHelper.EnsureHandle</c>), so
    /// measuring the whole frame now gives the HWND its final size before anyone reads its rectangle.
    /// </summary>
    void FitToContent()
    {
        if (SizeToContent == SizeToContent.Manual || VisualChildrenCount == 0 || GetVisualChild(0) is not UIElement root) return;
        var width = SizeToContent.HasFlag(SizeToContent.Width) || double.IsNaN(Width) ? double.PositiveInfinity : Width;
        var height = SizeToContent.HasFlag(SizeToContent.Height) || double.IsNaN(Height) ? double.PositiveInfinity : Height;
        root.Measure(new Size(width, height));
        if (SizeToContent.HasFlag(SizeToContent.Width)) Width = Math.Ceiling(root.DesiredSize.Width);
        if (SizeToContent.HasFlag(SizeToContent.Height)) Height = Math.Ceiling(root.DesiredSize.Height);
    }

    void PlayEnter()
    {
        if (_frame is null) return;
        var curve = WheelStyle.EaseOut;
        _frame.Opacity = 0;
        Motion.To(_frame, OpacityProperty, 1, Enter, curve);
        if (Motion.ReduceMotion) return;
        _scale.ScaleX = _scale.ScaleY = EnterScale;
        Motion.To(_scale, ScaleTransform.ScaleXProperty, 1, Enter, curve);
        Motion.To(_scale, ScaleTransform.ScaleYProperty, 1, Enter, curve);
    }

    void UpdateShadow()
    {
        if (_shadow is null) return;
        _shadow.Effect = new DropShadowEffect
        {
            BlurRadius = 32,
            ShadowDepth = 12,
            Direction = 270,
            Color = Colors.Black,
            Opacity = IsDarkSurface() ? DarkShadowOpacity : LightShadowOpacity,
        };
    }

    /// <summary>Dark theme = the applied <c>Zest.Window</c> colour is dark (#1D1B16 vs #FBFBF9).</summary>
    bool IsDarkSurface() =>
        GetValue(SurfaceProperty) is SolidColorBrush { Color: var c } && 0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B < 128;

    void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    void OnHeaderMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState != MouseButtonState.Pressed) return;
        e.Handled = true;
        DragMove();
    }

    static void OnHeadingChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var w = (ZestWindow)d;
        if (string.IsNullOrEmpty(w.Title) || w.Title == (string?)e.OldValue) w.Title = (string?)e.NewValue ?? "";
    }

    static void OnFooterChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var w = (ZestWindow)d;
        if (e.OldValue is not null) w.RemoveLogicalChild(e.OldValue);
        if (e.NewValue is not null) w.AddLogicalChild(e.NewValue);
    }

    static void OnIsResizableChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var w = (ZestWindow)d;
        if ((bool)e.NewValue)
        {
            w.ResizeMode = ResizeMode.CanResize;
            // The resize band is measured from the HWND edge. Its outer part is fully transparent (clicks fall through a
            // layered window there); the template paints the inner 8 px, just outside the frame, at alpha 1 so it is the grip.
            WindowChrome.SetWindowChrome(w, new WindowChrome
            {
                ResizeBorderThickness = new Thickness(ShadowMargin),
                CaptionHeight = 0,
                GlassFrameThickness = new Thickness(0),
                CornerRadius = new CornerRadius(0),
                UseAeroCaptionButtons = false,
            });
        }
        else
        {
            w.ResizeMode = ResizeMode.NoResize;
            w.ClearValue(WindowChrome.WindowChromeProperty);
        }
    }
}
