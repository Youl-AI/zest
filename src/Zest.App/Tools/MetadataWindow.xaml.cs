using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using Zest.App.Theme;
using Zest.Core.Catalog;
using Zest.Core.Metadata;
using Zest.Core.Output;

namespace Zest.App.Tools;

/// <summary>
/// Metadata of one file, editable, or the two clean-ups for several files. One file: a location banner (only when a tag is
/// a location tag) whose 위치 지우기 strikes the location rows, then the tags by group, each row a label, its value (a field
/// when the text can be changed) and a × for removable rows; 태그 모두 지우기 on the bottom bar dims the list. Several files:
/// 위치만 지우기 / 태그 모두 지우기. 새 파일로 저장 hands the edit to the caller, who starts one job per file.
/// </summary>
public partial class MetadataWindow : ZestWindow
{
    /// <summary>Groups in the order they are listed; any other group comes after them.</summary>
    static readonly string[] GroupOrder = ["파일", "카메라", "촬영", "위치", "설명", "문서", "미디어", "기타"];
    const string LocationGroup = "위치";

    readonly IReadOnlyList<string> _files;
    readonly IReadOnlyList<MetadataTag>? _tags;
    readonly Action<MetadataEdit> _save;
    readonly Dictionary<string, TextBox> _fields = new(StringComparer.Ordinal);
    readonly HashSet<string> _removed = new(StringComparer.Ordinal);
    /// <summary>Every row, to restyle it when it is struck or restored.</summary>
    readonly List<Row> _rows = [];
    bool _removeLocation;

    sealed record Row(MetadataTag Tag, TextBlock Key, FrameworkElement Value, Button? Remove);

    /// <param name="files">The dropped files (one: the editor; several: batch mode).</param>
    /// <param name="tags">The one file's tags as read; null = batch mode.</param>
    /// <param name="save">Starts the jobs with the edit as chosen.</param>
    public MetadataWindow(IReadOnlyList<string> files, IReadOnlyList<MetadataTag>? tags, Action<MetadataEdit> save)
    {
        _files = files;
        _tags = tags;
        _save = save;
        InitializeComponent();

        if (tags is null)
        {
            Heading = $"메타데이터 · 파일 {files.Count}개";
            SinglePanel.Visibility = Visibility.Collapsed;
            WipeBox.Visibility = Visibility.Collapsed;
            LocationOnlyRadio.IsChecked = true;
            LocationOnlyRadio.Checked += (_, _) => UpdateCaption();
            EverythingRadio.Checked += (_, _) => UpdateCaption();
        }
        else
        {
            BatchPanel.Visibility = Visibility.Collapsed;
            Banner.Visibility = tags.Any(t => MetadataEditBuilder.IsLocation(t.Id)) ? Visibility.Visible : Visibility.Collapsed;
            BuildList(tags);
            LocationButton.Click += (_, _) => SetRemoveLocation(!_removeLocation);
            WipeBox.Checked += (_, _) => UpdateWipe();
            WipeBox.Unchecked += (_, _) => UpdateWipe();
            SetRemoveLocation(false);
        }

        SaveButton.Click += (_, _) => Save();
        CancelButton.Click += (_, _) => Close();
        UpdateCaption();
    }

    /// <summary>Render harness: the banner toggled, the wipe switch, struck rows, or the batch's second choice.</summary>
    internal void Preview(bool removeLocation = false, bool removeAll = false, params string[] struck)
    {
        if (_tags is null)
        {
            (removeAll ? EverythingRadio : LocationOnlyRadio).IsChecked = true;
            return;
        }
        foreach (var id in struck) ToggleRemoved(id);
        SetRemoveLocation(removeLocation);
        WipeBox.IsChecked = removeAll;
    }

    bool RemoveAll => _tags is null ? EverythingRadio.IsChecked == true : WipeBox.IsChecked == true;

    // ---- The list ----

    void BuildList(IReadOnlyList<MetadataTag> tags)
    {
        var groups = tags.GroupBy(t => t.Group)
            .OrderBy(g => Array.IndexOf(GroupOrder, g.Key) is var i and >= 0 ? i : GroupOrder.Length);
        var first = true;
        foreach (var group in groups)
        {
            var section = new StackPanel();
            var box = new Border { Padding = new Thickness(0, 10, 0, 10), Child = section };
            if (!first)
            {
                box.BorderThickness = new Thickness(0, 1, 0, 0);
                box.SetResourceReference(Border.BorderBrushProperty, "Zest.Line");
            }
            first = false;

            var heading = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
            if (group.Key == LocationGroup && group.Any(t => MetadataEditBuilder.IsLocation(t.Id)))
                heading.Children.Add(LocationChip());
            heading.Children.Add(new TextBlock { Text = group.Key, Style = (Style)FindResource("GroupHeading") });
            section.Children.Add(heading);

            foreach (var tag in group) section.Children.Add(BuildRow(tag));
            GroupsPanel.Children.Add(box);
        }
    }

    /// <summary>"고칠 수 없음 · 통째로 지우기만" on the right of the 위치 heading: GPS goes only through the banner.</summary>
    Border LocationChip()
    {
        var chip = new Border { Style = (Style)FindResource("Zest.Chip") };
        DockPanel.SetDock(chip, Dock.Right);
        chip.SetResourceReference(Border.BackgroundProperty, "Zest.Seg");
        var text = new TextBlock { Text = "고칠 수 없음 · 통째로 지우기만", Style = (Style)FindResource("Zest.ChipText") };
        text.SetResourceReference(TextBlock.ForegroundProperty, "Zest.Text2");
        chip.Child = text;
        return chip;
    }

    FrameworkElement BuildRow(MetadataTag tag)
    {
        var grid = new Grid { MinHeight = 36 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(96) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });

        var key = new TextBlock { Text = tag.Label, Style = (Style)FindResource("Label"), TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = tag.Label };
        grid.Children.Add(key);

        FrameworkElement value;
        if (tag.Editable)
        {
            var field = new TextBox { Text = tag.Value, Style = (Style)FindResource("Zest.Field") };
            AutomationPropertiesName(field, tag.Label);
            _fields[tag.Id] = field;
            value = WithPlaceholder(field);
        }
        else
        {
            value = new TextBlock { Text = tag.Value, Style = (Style)FindResource("TagValue"), ToolTip = tag.Value.Length > 0 ? tag.Value : null };
        }
        Grid.SetColumn(value, 2);
        grid.Children.Add(value);

        // Location rows have no × of their own: GPS is removed as a whole with the banner.
        Button? remove = null;
        if (tag.Removable && !MetadataEditBuilder.IsLocation(tag.Id))
        {
            remove = new Button { Style = (Style)FindResource("RemoveButton"), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(remove, 4);
            remove.Click += (_, _) => ToggleRemoved(tag.Id);
            grid.Children.Add(remove);
        }

        var row = new Row(tag, key, value, remove);
        _rows.Add(row);
        Restyle(row);
        return grid;
    }

    static void AutomationPropertiesName(DependencyObject element, string name) =>
        System.Windows.Automation.AutomationProperties.SetName(element, name);

    /// <summary>An empty field shows "비어 있음" in text-3 until something is typed.</summary>
    static FrameworkElement WithPlaceholder(TextBox field)
    {
        var hint = new TextBlock
        {
            Text = "비어 있음",
            Margin = new Thickness(9, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false,
            FontFamily = field.FontFamily,
            FontSize = field.FontSize,
        };
        hint.SetResourceReference(TextBlock.ForegroundProperty, "Zest.Text3");
        void Update() => hint.Visibility = field.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        field.TextChanged += (_, _) => Update();
        Update();
        var host = new Grid();
        host.Children.Add(field);
        host.Children.Add(hint);
        return host;
    }

    void ToggleRemoved(string id)
    {
        if (!_removed.Add(id)) _removed.Remove(id);
        foreach (var row in _rows.Where(r => r.Tag.Id == id)) Restyle(row);
    }

    bool IsStruck(MetadataTag tag) => _removed.Contains(tag.Id) || (_removeLocation && MetadataEditBuilder.IsLocation(tag.Id));

    /// <summary>A struck row: label and value crossed out at 45 %, its field read-only; the × says how to undo it.</summary>
    void Restyle(Row row)
    {
        var struck = IsStruck(row.Tag);
        var decorations = struck ? TextDecorations.Strikethrough : null;
        var opacity = struck ? 0.45 : 1;
        row.Key.TextDecorations = decorations;
        row.Key.Opacity = opacity;
        row.Value.Opacity = opacity;
        if (row.Value is TextBlock text) text.TextDecorations = decorations;
        if (_fields.TryGetValue(row.Tag.Id, out var field))
        {
            field.TextDecorations = decorations;
            field.IsReadOnly = struck;
        }
        if (row.Remove is { } remove)
        {
            var name = _removed.Contains(row.Tag.Id) ? $"{row.Tag.Label} 되돌리기" : $"{row.Tag.Label} 지우기";
            remove.ToolTip = name;
            AutomationPropertiesName(remove, name);
        }
    }

    // ---- Banner, wipe, caption ----

    void SetRemoveLocation(bool on)
    {
        _removeLocation = on;
        BannerText.Inlines.Clear();
        if (on)
        {
            BannerText.Inlines.Add(new Run("저장할 때 "));
            BannerText.Inlines.Add(new Bold(new Run("촬영 위치")));
            BannerText.Inlines.Add(new Run("를 뺍니다."));
        }
        else
        {
            // WPF breaks Korean inside words, so the second sentence starts its own line.
            BannerText.Inlines.Add(new Run("이 파일에는 "));
            BannerText.Inlines.Add(new Bold(new Run("촬영 위치")));
            BannerText.Inlines.Add(new Run("가 들어 있습니다."));
            BannerText.Inlines.Add(new LineBreak());
            BannerText.Inlines.Add(new Run("공유하기 전에 지우는 것이 좋습니다."));
        }
        LocationButton.Content = on ? "되돌리기" : "위치 지우기";
        Banner.SetResourceReference(Border.BackgroundProperty, on ? "Zest.Lime.Wash" : "Zest.Tangerine.Wash");
        var ink = on ? "Zest.Lime.Ink" : "Zest.Tangerine.Ink";
        PinPath.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, ink);
        PinDot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, ink);
        foreach (var row in _rows) Restyle(row);
    }

    /// <summary>태그 모두 지우기: the list fades to 40 % and takes no mouse or keyboard until it is turned off.</summary>
    void UpdateWipe()
    {
        var wipe = RemoveAll;
        ListScroll.Opacity = wipe ? 0.4 : 1;
        ListScroll.IsHitTestVisible = !wipe;
        foreach (var field in _fields.Values) field.IsTabStop = !wipe;
        foreach (var row in _rows) if (row.Remove is { } remove) remove.IsTabStop = !wipe;
        UpdateCaption();
    }

    /// <summary>
    /// `원본은 그대로 두고 옆에 ‘{stem} (edited).{ext}’를 만듭니다.` for the first file; with the wipe on, `(no metadata)` and,
    /// for images, the line saying the colour profile and orientation stay.
    /// </summary>
    void UpdateCaption()
    {
        var file = _files[0];
        var wipe = RemoveAll;
        var name = $"{OutputNaming.Stem(file)} ({(wipe ? "no metadata" : "edited")}).{OutputExtension(file)}";
        var text = $"원본은 그대로 두고 옆에 ‘{name}’를 만듭니다.";
        if (wipe && _files.Any(IsImage)) text += "\n색 정보(ICC)와 사진 방향은 남깁니다.";
        CaptionText.Text = text;
    }

    static bool IsImage(string path) => FormatCatalog.FormatOf(path) is { } f && FormatCatalog.CategoryOf(f) == FormatCategory.Image;

    /// <summary>The extension the writer gives the copy: the canonical one for images and PDFs (jpeg → jpg), the file's own for media.</summary>
    static string OutputExtension(string path)
    {
        var own = Path.GetExtension(path).TrimStart('.');
        return FormatCatalog.FormatOf(path) is { } f && FormatCatalog.CategoryOf(f) is FormatCategory.Image or FormatCategory.Pdf ? f : own;
    }

    // ---- Save ----

    void Save()
    {
        MetadataEdit edit;
        if (_tags is null)
        {
            edit = RemoveAll ? MetadataEdit.Everything : MetadataEdit.LocationOnly;
        }
        else
        {
            var current = _fields.ToDictionary(f => f.Key, f => f.Value.Text, StringComparer.Ordinal);
            edit = MetadataEditBuilder.Build(_tags, current, _removed, _removeLocation, RemoveAll);
        }
        _save(edit);
        Close();
    }
}
