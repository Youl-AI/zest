using LimeWin.Core.Convert;

namespace LimeWin.Core.Tests;

public class MarkdownBridgeTests
{
    [Fact]
    public void Markdown_RoundTrip_KeepsHeadingListBoldItalic()
    {
        var md = "# 제목\n\n- 하나\n- **둘**\n\n*기울임*";
        var back = MarkdownBridge.FromHtml(MarkdownBridge.ToHtml(md));
        Assert.Contains("# 제목", back);
        Assert.Contains("**둘**", back);
        Assert.Matches(@"[*_]기울임[*_]", back);
    }

    [Fact]
    public void ToHtml_UsesAdvancedExtensions_PipeTable()
    {
        var html = MarkdownBridge.ToHtml("| a | b |\n|---|---|\n| 1 | 2 |");
        Assert.Contains("<table>", html);
    }

    [Fact]
    public void FromHtml_FullDocument_DropsHeadAndStyle()
    {
        // Shape of Word's filtered HTML: a full document with a <head> full of CSS.
        var html = "<html><head><meta charset=\"utf-8\"><title>t</title><style>p.MsoNormal{margin:0}</style></head>" +
                   "<body lang=KO><div class=WordSection1><h1>제목</h1><p class=MsoNormal>안녕 <b>굵게</b></p></div></body></html>";
        var md = MarkdownBridge.FromHtml(html);
        Assert.Contains("# 제목", md);
        Assert.Contains("**굵게**", md);
        Assert.DoesNotContain("MsoNormal", md);
        Assert.DoesNotContain("<head", md, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<title", md, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FromHtml_PassesUnknownTagsThrough()
    {
        var md = MarkdownBridge.FromHtml("<p>a <kbd>Ctrl</kbd> b</p>");
        Assert.Contains("<kbd>Ctrl</kbd>", md);
    }
}
