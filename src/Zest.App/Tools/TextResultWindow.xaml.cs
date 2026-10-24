using System.IO;
using System.Windows;
using Zest.App.Actions;
using Zest.Core.Output;
using Zest.Core.Tools;
using Wpf.Ui.Controls;

namespace Zest.App.Tools;

/// <summary>The text read from one or more files: editable, with 복사, 텍스트로 저장 and 닫기. With no text it only says so.</summary>
public partial class TextResultWindow : FluentWindow
{
    readonly Action<string> _save;

    /// <param name="copiedAtOnce">Whether the text is already on the clipboard (says so in the status line, otherwise says why not).</param>
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
            StatusText.Visibility = Visibility.Collapsed;
            SaveButton.IsEnabled = CopyButton.IsEnabled = false;
        }
        else
        {
            StatusText.Text = copiedAtOnce ? "클립보드에 복사했습니다. 고친 내용은 ‘복사’를 다시 누르세요." : ActionRunner.ClipboardBusyMessage;
        }

        CopyButton.Click += (_, _) => Copy();
        SaveButton.Click += (_, _) =>
        {
            _save(ResultBox.Text);
            StatusText.Text = "텍스트 파일로 저장합니다. 진행 카드를 확인하세요.";
        };
        CloseButton.Click += (_, _) => Close();
    }

    void Copy()
    {
        var data = new DataObject();
        data.SetText(OcrService.WindowsNewlines(ResultBox.Text));
        StatusText.Text = ActionRunner.TrySetClipboard(data) ? "클립보드에 복사했습니다." : ActionRunner.ClipboardBusyMessage;
    }
}
