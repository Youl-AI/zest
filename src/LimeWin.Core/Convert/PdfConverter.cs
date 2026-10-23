using System.Text;
using LimeWin.Core.Jobs;
using LimeWin.Core.Output;
using LimeWin.Core.Pdf;

namespace LimeWin.Core.Convert;

/// <summary>PDF to jpg/png (one file, or a "(pages)" folder for several pages) and PDF to txt.</summary>
public sealed class PdfConverter : IConverter
{
    const int Dpi = 200;
    const int MaxEdgePx = 4096;

    public bool Handles(string from, string to) => from == "pdf" && to is "jpg" or "png" or "txt";

    public async Task<IReadOnlyList<string>> ConvertAsync(string source, string to, OperationContext ctx)
    {
        ctx.Token.ThrowIfCancellationRequested();
        return to == "txt" ? [await ToTextAsync(source, ctx)] : await ToImagesAsync(source, to, ctx);
    }

    static async Task<IReadOnlyList<string>> ToImagesAsync(string source, string to, OperationContext ctx)
    {
        var count = await PdfRenderer.PageCountAsync(source);
        if (count < 1) throw new LimeException("PDF를 읽을 수 없습니다.");

        var output = count == 1 ? OutputNaming.ConvertOutput(source, to, ctx) : OutputNaming.CreatePagesFolder(source, ctx);
        await PdfRenderer.RenderPagesAsync(source, output, to, Dpi, MaxEdgePx, ctx);
        return [output];
    }

    static async Task<string> ToTextAsync(string source, OperationContext ctx)
    {
        var pages = await Task.Run(() => PdfText.PageTexts(source, ctx.Token, ctx.Report), ctx.Token);
        if (pages.All(string.IsNullOrWhiteSpace))
            throw new LimeException("이 PDF에는 글자 정보가 없습니다. 도구 휠의 '글자 복사'를 쓰세요.");

        var output = OutputNaming.ConvertOutput(source, "txt", ctx);
        await File.WriteAllTextAsync(output, string.Join("\n\n", pages), new UTF8Encoding(false), ctx.Token);
        return output;
    }
}
