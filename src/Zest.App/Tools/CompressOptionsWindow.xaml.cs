using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Zest.App.Theme;
using Zest.Core;
using Zest.Core.Catalog;
using Zest.Core.Output;
using Zest.Core.Tools;

namespace Zest.App.Tools;

/// <summary>A file to compress and its size in bytes (null when it could not be read; the job then reports it).</summary>
public sealed record CompressSource(string Path, long? Bytes);

/// <summary>
/// Compress options for one or more images: the mode is one segment row (프리셋 / 목표 크기 / 비율) and only the chosen mode's
/// panel shows under it. 압축 hands the same options to the caller, who starts one job per file.
/// </summary>
public partial class CompressOptionsWindow : ZestWindow
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
        SizeValue.Text = _total is { } t ? SizeText.Format(t) : "알 수 없음";
        HintText.Text = files.Count == 1 ? $"옆에 ‘{OutputNameOf(files[0].Path)}’로 저장" : "";

        foreach (var (_, label) in Caps) CapBox.Items.Add(new ComboBoxItem { Content = label, FontFamily = Wheel.WheelStyle.TextFont });

        Restore(_last);

        foreach (var radio in new[] { PresetRadio, TargetRadio, PercentRadio }) radio.Checked += (_, _) => ShowGoalPanel();
        PercentSlider.ValueChanged += (_, _) => UpdateEstimate();
        TargetBox.TextChanged += (_, _) => Error = "";
        CompressButton.Click += (_, _) => Start();
        ShowGoalPanel();
        UpdateEstimate();
    }

    /// <summary>Render harness: a mode picked and, optionally, an error on the bottom bar.</summary>
    internal void Preview(SizeGoal goal, string targetText = "", string error = "")
    {
        (goal switch { SizeGoal.FileSize => TargetRadio, SizeGoal.Percentage => PercentRadio, _ => PresetRadio }).IsChecked = true;
        TargetBox.Text = targetText;
        Error = error;
    }

    /// <summary>`name (compressed).ext` as the compressor names it (its canonical extension, e.g. jpeg → jpg).</summary>
    static string OutputNameOf(string path) =>
        $"{OutputNaming.Stem(path)} (compressed).{FormatCatalog.FormatOf(path) ?? Path.GetExtension(path).TrimStart('.')}";

    /// <summary>The bottom bar's error line; while it shows, the save hint is hidden.</summary>
    string Error
    {
        set
        {
            ErrorText.Text = value;
            HintText.Visibility = value.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    void Restore(Remembered r)
    {
        (r.Goal switch { SizeGoal.FileSize => TargetRadio, SizeGoal.Percentage => PercentRadio, _ => PresetRadio }).IsChecked = true;
        (r.Level == SquashLevel.Hard ? HardRadio : GentleRadio).IsChecked = true;
        TargetBox.Text = r.TargetText;
        UnitBox.SelectedIndex = r.Unit == SizeUnit.MB ? 1 : 0;
        PercentSlider.Value = r.Percent;
        CapBox.SelectedIndex = Math.Max(0, Array.FindIndex(Caps, c => c.Cap == r.Cap));
    }

    SizeGoal Goal => TargetRadio.IsChecked == true ? SizeGoal.FileSize : PercentRadio.IsChecked == true ? SizeGoal.Percentage : SizeGoal.Preset;
    SizeUnit Unit => UnitBox.SelectedIndex == 1 ? SizeUnit.MB : SizeUnit.KB;

    /// <summary>Only the chosen mode's controls show; the window follows the panel's height (SizeToContent).</summary>
    void ShowGoalPanel()
    {
        var goal = Goal;
        PresetPanel.Visibility = goal == SizeGoal.Preset ? Visibility.Visible : Visibility.Collapsed;
        TargetPanel.Visibility = goal == SizeGoal.FileSize ? Visibility.Visible : Visibility.Collapsed;
        PercentPanel.Visibility = goal == SizeGoal.Percentage ? Visibility.Visible : Visibility.Collapsed;
        Error = "";
    }

    void UpdateEstimate()
    {
        PercentText.Text = string.Create(CultureInfo.InvariantCulture, $"{PercentSlider.Value:0}%");
        EstimateText.Visibility = _total is null ? Visibility.Collapsed : Visibility.Visible;
        if (_total is { } total)
            EstimateValue.Text = SizeText.Format(new CompressOptions(SizeGoal.Percentage, ShrinkPercent: PercentSlider.Value).TargetFor(total) ?? 0);
    }

    void Start()
    {
        Error = "";
        CompressOptions options;
        try
        {
            options = Build();
            // One file: an unusable target is said here, before anything starts. Several files: every file starts and
            // one whose target does not fit fails on its own card with the compressor's message (controller ruling).
            if (_files is [{ Bytes: { } bytes }]) options.CheckTarget(bytes);
        }
        catch (ZestException e)
        {
            Error = e.Message;
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
            HardRadio.IsChecked == true ? SquashLevel.Hard : SquashLevel.Gentle,
            PercentSlider.Value,
            goal == SizeGoal.FileSize ? TargetSize.Parse(TargetBox.Text, Unit) : null,
            Caps[Math.Max(0, CapBox.SelectedIndex)].Cap);
    }
}
