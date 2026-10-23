using System.Runtime.InteropServices;
using System.Text;
using LimeWin.Core.Catalog;
using LimeWin.Core.Convert;
using LimeWin.Core.Jobs;
using LimeWin.Core.Output;

namespace LimeWin.Core.Office;

/// <summary>
/// Spreadsheets through Excel (xlsx xls ods csv tsv to each other and pdf; xls input only). csv/tsv are read in the
/// encoding their content shows (<see cref="TextOrigin"/>) and written from the first sheet as UTF-8 without BOM. Excel reads a private copy of the source and writes only inside a
/// private temp folder; the finished file is then moved (or rewritten) next to the source.
/// </summary>
public sealed class ExcelConverter(OfficeHost excel) : IConverter
{
    static readonly TimeSpan Timeout = TimeSpan.FromMinutes(2); // spec §7
    static readonly HashSet<string> Sources = ["xlsx", "xls", "ods", "csv", "tsv"];
    static readonly HashSet<string> Targets = ["xlsx", "ods", "csv", "tsv", "pdf"];
    static readonly UTF8Encoding Utf8 = new(false);

    public bool Handles(string from, string to) => Sources.Contains(from) && Targets.Contains(to) && from != to;

    public async Task<IReadOnlyList<string>> ConvertAsync(string source, string to, OperationContext ctx)
    {
        ctx.Token.ThrowIfCancellationRequested();
        var from = FormatCatalog.FormatOf(source) ?? "";
        OfficeWorkFolder.EnsureNotEmpty(source);
        using var work = OfficeWorkFolder.Create();
        // OpenText ignores its encoding and delimiter arguments for a ".csv" file (measured: UTF-8 read as ANSI),
        // so text sources are opened from a ".txt" copy.
        var text = from is "csv" or "tsv";
        var input = work.CopySource(source, text ? ".txt" : Path.GetExtension(source));
        var origin = text ? await Task.Run(() => TextOrigin(input), ctx.Token) : 0;
        var output = OutputNaming.ConvertOutput(source, to, ctx);
        var saved = Path.Combine(work.Out, Path.GetFileName(output));

        ctx.Report(0.05);
        await excel.RunAsync(app => { SaveWithExcel(app, input, from, origin, saved, to, OutputNaming.Stem(source)); return 0; }, Timeout, ctx.Token);
        ctx.Token.ThrowIfCancellationRequested();
        ctx.Report(0.9);

        switch (to)
        {
            case "csv": // xlCSVUTF8 writes a BOM; reading as UTF-8 drops it
                await File.WriteAllTextAsync(output, await File.ReadAllTextAsync(saved, Encoding.UTF8, ctx.Token), Utf8, ctx.Token);
                break;
            case "tsv": // xlUnicodeText is UTF-16
                await File.WriteAllTextAsync(output, await File.ReadAllTextAsync(saved, Encoding.Unicode, ctx.Token), Utf8, ctx.Token);
                break;
            default:
                File.Move(saved, output, overwrite: true); // onto our own placeholder only
                break;
        }
        ctx.Report(1);
        return [output];
    }

    /// <summary>
    /// The code page OpenText reads a text source with, judged by content: UTF-8 after a UTF-8 or UTF-16 LE byte order
    /// mark, else UTF-8 when the whole file decodes as strict UTF-8, else CP949 (Korean Excel's own "CSV (쉼표로 분리)").
    /// A UTF-16 LE file still gets 65001: OpenText rejects Origin 1200 and reads a UTF-16 LE BOM by itself (measured).
    /// </summary>
    internal static int TextOrigin(string path)
    {
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16);
        var head = new byte[3];
        var n = file.ReadAtLeast(head, 3, throwOnEndOfStream: false);
        if (n == 3 && head is [0xEF, 0xBB, 0xBF]) return MsoConst.EncodingUtf8;
        if (n >= 2 && head[0] == 0xFF && head[1] == 0xFE) return MsoConst.EncodingUtf8;
        file.Position = 0;
        var decoder = new UTF8Encoding(false, throwOnInvalidBytes: true).GetDecoder();
        var buffer = new byte[1 << 16];
        try
        {
            int read;
            while ((read = file.Read(buffer)) > 0) decoder.GetCharCount(buffer, 0, read, flush: false);
            decoder.GetCharCount([], 0, 0, flush: true); // a sequence cut off at the end is invalid too
            return MsoConst.EncodingUtf8;
        }
        catch (DecoderFallbackException) { return KoreanCodePage; }
    }

    const int KoreanCodePage = 949;

    /// <summary>Runs on the Office STA thread.</summary>
    static void SaveWithExcel(dynamic app, string input, string from, int origin, string saved, string to, string stem)
    {
        dynamic books = app.Workbooks;
        dynamic? book = null;
        try
        {
            try
            {
                if (from is "csv" or "tsv")
                {
                    books.OpenText(Filename: input, Origin: origin, DataType: ExcelConst.Delimited,
                        Tab: from == "tsv", Comma: from == "csv", Local: false);
                    book = books.Item(Path.GetFileName(input));
                }
                else
                {
                    book = books.Open(Filename: input, UpdateLinks: 0, ReadOnly: true, Password: ExcelConst.WrongPassword,
                        IgnoreReadOnlyRecommended: true, AddToMru: false);
                }
            }
            catch (COMException) when (OfficeEncryption.IsEncryptedPackage(input)) // Excel reports it like a corrupt file
            {
                throw new LimeException("암호로 보호된 문서는 열 수 없습니다.");
            }
            catch (COMException)
            {
                CloseOurCopy(books, input, saved); // OpenText may have opened it before Item failed
                throw new LimeException("문서를 열 수 없습니다.");
            }

            try
            {
                if ((bool)book!.HasVBProject) throw new LimeException("매크로가 들어 있는 문서는 변환하지 않습니다.");
                dynamic sheets = book.Worksheets;
                dynamic? first = null;
                try
                {
                    first = sheets.Item(1);
                    // A text file's one sheet is named after the file Excel opened: the private copy's GUID.
                    if (from is "csv" or "tsv" && SheetName(stem) is { } name)
                    {
                        try { first.Name = name; }
                        catch (COMException) { }
                    }
                    switch (to)
                    {
                        case "pdf":
                            book.ExportAsFixedFormat(Type: ExcelConst.TypePdf, Filename: saved);
                            break;
                        case "csv" or "tsv": // these formats hold one sheet: the active one is saved
                            first.Activate();
                            book.SaveAs(Filename: saved, FileFormat: to == "csv" ? ExcelFormat.CsvUtf8 : ExcelFormat.UnicodeText, AddToMru: false);
                            break;
                        default:
                            book.SaveAs(Filename: saved, FileFormat: to == "ods" ? ExcelFormat.Ods : ExcelFormat.Xlsx, AddToMru: false);
                            break;
                    }
                }
                finally
                {
                    if (first is not null) Marshal.FinalReleaseComObject(first);
                    Marshal.FinalReleaseComObject(sheets);
                }
            }
            catch (COMException e) { throw new LimeException($"변환 실패: {e.Message}"); }
        }
        finally
        {
            if (book is not null)
            {
                try { book.Close(SaveChanges: false); }
                catch (COMException) { CloseOurCopy(books, input, saved); }
                Marshal.FinalReleaseComObject(book);
            }
            Marshal.FinalReleaseComObject(books);
        }
    }

    /// <summary>
    /// Best effort: closes any open workbook that is our private copy, matched by its full path before or after SaveAs
    /// (never by name: the user may have a workbook open under the very name we saved as, e.g. "가계부.csv").
    /// </summary>
    static void CloseOurCopy(dynamic books, string input, string saved)
    {
        try
        {
            foreach (dynamic b in books)
            {
                try
                {
                    if (OfficeWorkFolder.IsOurCopy((string)b.FullName, input, saved)) b.Close(SaveChanges: false);
                }
                catch (COMException) { }
                finally { Marshal.FinalReleaseComObject(b); }
            }
        }
        catch (COMException) { }
    }

    /// <summary>A valid sheet name (at most 31 characters, none of <c>[]:*?/\</c>, no edge apostrophes) or null.</summary>
    static string? SheetName(string stem)
    {
        var name = new string(stem.Select(c => "[]:*?/\\".Contains(c) ? '_' : c).ToArray()).Trim('\'');
        if (name.Length > 31) name = name[..31];
        return name.Length == 0 ? null : name;
    }
}
