using System.Globalization;
using System.IO;
using System.Windows.Controls;
using LimeWin.Core;
using LimeWin.Core.Tools;
using Wpf.Ui.Controls;

namespace LimeWin.App.Tools;

/// <summary>A file to compress and its size in bytes (null when it could not be read; the job then reports it).</summary>
public sealed record CompressSource(string Path, long? Bytes);

/// <summary>Compress options for one or more images; 압축 hands the same options to the caller, who starts one job per file.</summary>
public partial class CompressOptionsWindow : FluentWindow
{
    /// <summary>The options last used to start a compression, for this session only (never saved).</summary>
    sealed record Remembered(SizeGoal Goal, SquashLevel Level, string TargetText, SizeUnit Unit, double Percent, ResizeCap Cap);

    static Remembered _last = new(SizeGoal.Preset, SquashLevel.Gentle, "", SizeUnit.KB, 50, ResizeCap.Original);

    static readonly (ResizeCap Cap, string Label)[] Caps =
    [
        (ResizeCap.Original, "원본"),
        (ResizeCap.Px2560, "2560 px"),
        (ResizeCap.Px1920, "1920 px"),
        (ResizeCap.Px1280, "1280 px"),
    ];

    readonly IReadOnlyList<CompressSource> _files;
    readonly Action<CompressOptions> _start;
    readonly long? _total;

    public CompressOptionsWindow(IReadOnlyList<CompressSource> files, Action<CompressOptions> start)
    {
        _files = files;
        _start = start;
        _total = files.All(f => f.Bytes is not null) ? files.Sum(f => f.Bytes!.Value) : null;
        InitializeComponent();

        var first = Path.GetFileName(files[0].Path);
        FileNameText.Text = files.Count == 1 ? first : $"{first} 외 {files.Count - 1}개";
        SizeCaption.Text = files.Count == 1 ? "원본 크기" : "합계";
        SizeValue.Text = _total is { } t ? ImageCompressor.FormatSize(t) : "알 수 없음";

        foreach (var (_, label) in Caps) CapBox.Items.Add(new ComboBoxItem { Content = label, FontFamily = Wheel.WheelStyle.TextFont });

        Restore(_last);

        Selects(LevelBox, PresetRadio);
        Selects(TargetBox, TargetRadio);
        Selects(UnitBox, TargetRadio);
        Selects(PercentSlider, PercentRadio);
        PercentSlider.ValueChanged += (_, _) => UpdateEstimate();
        TargetBox.TextChanged += (_, _) => ErrorText.Text = "";
        CompressButton.Click += (_, _) => Start();
        UpdateEstimate();
    }

    void Restore(Remembered r)
    {
        (r.Goal switch { SizeGoal.FileSize => TargetRadio, SizeGoal.Percentage => PercentRadio, _ => PresetRadio }).IsChecked = true;
        LevelBox.SelectedIndex = r.Level == SquashLevel.Hard ? 1 : 0;
        TargetBox.Text = r.TargetText;
        UnitBox.SelectedIndex = r.Unit == SizeUnit.MB ? 1 : 0;
        PercentSlider.Value = r.Percent;
        CapBox.SelectedIndex = Math.Max(0, Array.FindIndex(Caps, c => c.Cap == r.Cap));
    }

    /// <summary>Touching a row's control picks that row's goal, so there is no need to click the radio first.</summary>
    static void Selects(Control control, RadioButton radio)
    {
        control.PreviewMouseLeftButtonDown += (_, _) => radio.IsChecked = true;
        control.GotKeyboardFocus += (_, _) => radio.IsChecked = true;
    }

    SizeGoal Goal => TargetRadio.IsChecked == true ? SizeGoal.FileSize : PercentRadio.IsChecked == true ? SizeGoal.Percentage : SizeGoal.Preset;
    SizeUnit Unit => UnitBox.SelectedIndex == 1 ? SizeUnit.MB : SizeUnit.KB;

    void UpdateEstimate()
    {
        PercentText.Text = string.Create(CultureInfo.InvariantCulture, $"{PercentSlider.Value:0} %");
        // Always shown under the slider (it describes that row), so switching goals never changes the window's height.
        EstimateText.Text = _total is { } total
            ? $"예상 {ImageCompressor.FormatSize(new CompressOptions(SizeGoal.Percentage, ShrinkPercent: PercentSlider.Value).TargetFor(total) ?? 0)} 이하"
            : "";
    }

    void Start()
    {
        ErrorText.Text = "";
        CompressOptions options;
        try
        {
            options = Build();
            foreach (var file in _files)
            {
                if (file.Bytes is not { } bytes) continue;
                try
                {
                    options.CheckTarget(bytes);
                }
                catch (LimeException e) when (_files.Count > 1)
                {
                    throw new LimeException($"{Path.GetFileName(file.Path)}: {e.Message}");
                }
            }
        }
        catch (LimeException e)
        {
            ErrorText.Text = e.Message;
            if (Goal == SizeGoal.FileSize)
            {
                TargetBox.Focus();
                TargetBox.SelectAll();
            }
            return;
        }

        _last = new Remembered(options.Goal, options.Level, TargetBox.Text, Unit, PercentSlider.Value, options.Cap);
        _start(options);
        Close();
    }

    CompressOptions Build()
    {
        var goal = Goal;
        return new CompressOptions(
            goal,
            LevelBox.SelectedIndex == 1 ? SquashLevel.Hard : SquashLevel.Gentle,
            PercentSlider.Value,
            goal == SizeGoal.FileSize ? TargetSize.Parse(TargetBox.Text, Unit) : null,
            Caps[Math.Max(0, CapBox.SelectedIndex)].Cap);
    }
}
