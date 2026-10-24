using System.Windows;
using Zest.App.Actions;
using Zest.App.Theme;
using Zest.Core.Output;
using Zest.Core.Tools;

namespace Zest.App.Tools;

/// <summary>
/// The text read from one or more files: editable, with 복사, 텍스트로 저장 and 닫기. A lime "클립보드에 복사함" chip says the
/// clipboard holds the text as shown (an edit hides it until 복사); other news (the clipboard was busy, the save started)
/// goes on a line under the text. With no text it only says so.
/// </summary>
public partial class TextResultWindow : ZestWindow
{
    readonly Action<string> _save;

    /// <param name="copiedAtOnce">Whether the text is already on the clipboard (shows the chip, otherwise says why not).</param>
    /// <param name="save">Starts the save job with the text as currently edited.</param>
    public TextResultWindow(IReadOnlyList<string> sources, string text, bool copiedAtOnce, Action<string> save)
    {
        _save = save;
        InitializeComponent();

        var first = OutputNaming.DisplayName(sources[0]);
        SourceText.Text = sources.Count == 1 ? first : $"{first} 외 {sources.Count - 1}개";
        ResultBox.Text = text;

        if (text.Length == 0)
        {
            ResultBox.Visibility = Visibility.Collapsed;
            EmptyText.Visibility = Visibility.Visible;
            SaveButton.IsEnabled = CopyButton.IsEnabled = false;
        }
        else
        {
            ShowCopied(copiedAtOnce);
        }

        // The chip means "the clipboard holds what you see": an edit makes it stale until 복사 is pressed again.
        ResultBox.TextChanged += (_, _) => CopiedChip.Visibility = Visibility.Collapsed;
        CopyButton.Click += (_, _) => Copy();
        SaveButton.Click += (_, _) =>
        {
            _save(ResultBox.Text);
            Status("텍스트 파일로 저장합니다. 진행 카드를 확인하세요.", danger: false);
        };
        CloseButton.Click += (_, _) => Close();
    }

    void Copy()
    {
        var data = new DataObject();
        data.SetText(OcrService.WindowsNewlines(ResultBox.Text));
        ShowCopied(ActionRunner.TrySetClipboard(data));
    }

    /// <summary>Copied: the chip, no status line. Not copied: no chip, and the reason in the danger colour.</summary>
    void ShowCopied(bool copied)
    {
        CopiedChip.Visibility = copied ? Visibility.Visible : Visibility.Collapsed;
        Status(copied ? "" : ActionRunner.ClipboardBusyMessage, danger: true);
    }

    void Status(string message, bool danger)
    {
        StatusText.Text = message;
        StatusText.Visibility = message.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        StatusText.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, danger ? "Zest.Danger" : "Zest.Text2");
    }
}
