using Markdig;

namespace Zest.Core.Convert;

/// <summary>Markdown to HTML (Markdig, advanced extensions) and back (ReverseMarkdown, GitHub flavored).</summary>
public static class MarkdownBridge
{
    static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();

    /// <summary>An HTML fragment (no &lt;html&gt;/&lt;head&gt;); see <see cref="ToHtmlDocument"/> for a standalone file.</summary>
    public static string ToHtml(string markdown) => Markdown.ToHtml(markdown, Pipeline);

    /// <summary>A standalone UTF-8 HTML document wrapping <see cref="ToHtml"/>.</summary>
    public static string ToHtmlDocument(string markdown, string title) =>
        "<!DOCTYPE html>\n<html>\n<head>\n<meta charset=\"utf-8\">\n" +
        $"<title>{System.Net.WebUtility.HtmlEncode(title)}</title>\n</head>\n<body>\n{ToHtml(markdown)}</body>\n</html>\n";

    public static string FromHtml(string html)
    {
        var converter = new ReverseMarkdown.Converter(new ReverseMarkdown.Config
        {
            GithubFlavored = true,
            Tags = { Unknown = ReverseMarkdown.Config.UnknownTagsOption.PassThrough },
            Formatting = { RemoveComments = true },
        });
        return converter.Convert(html).Trim() + "\n";
    }
}
