using System.Runtime.InteropServices;
using System.Text;
using LimeWin.Core.Catalog;
using LimeWin.Core.Convert;
using LimeWin.Core.Jobs;
using LimeWin.Core.Output;

namespace LimeWin.Core.Office;

/// <summary>
/// Spreadsheets through Excel (xlsx xls ods csv tsv to each other and pdf; xls input only). csv/tsv are read as UTF-8 and
/// written from the first sheet as UTF-8 without BOM. Excel reads a private copy of the source and writes only inside a
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
        var output = OutputNaming.ConvertOutput(source, to);
        var saved = Path.Combine(work.Out, Path.GetFileName(output));

        ctx.Track(output);
        ctx.Report(0.05);
        await excel.RunAsync(app => { SaveWithExcel(app, input, from, saved, to, OutputNaming.Stem(source)); return 0; }, Timeout, ctx.Token);
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
                File.Move(saved, output);
                break;
        }
        ctx.Report(1);
        return [output];
    }

    /// <summary>Runs on the Office STA thread.</summary>
    static void SaveWithExcel(dynamic app, string input, string from, string saved, string to, string stem)
    {
        dynamic books = app.Workbooks;
        dynamic? book = null;
        try
        {
            try
            {
                if (from is "csv" or "tsv")
                {
                    books.OpenText(Filename: input, Origin: MsoConst.EncodingUtf8, DataType: ExcelConst.Delimited,
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
                throw new LimeException("문서를 열 수 없습니다.");
            }

            if ((bool)book!.HasVBProject) throw new LimeException("매크로가 들어 있는 문서는 변환하지 않습니다.");

            dynamic sheets = book.Worksheets;
            dynamic first = sheets.Item(1);
            try
            {
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
            catch (COMException e) { throw new LimeException($"변환 실패: {e.Message}"); }
            finally
            {
                Marshal.FinalReleaseComObject(first);
                Marshal.FinalReleaseComObject(sheets);
            }
        }
        finally
        {
            if (book is not null)
            {
                try { book.Close(SaveChanges: false); }
                catch (COMException) { }
                Marshal.FinalReleaseComObject(book);
            }
            Marshal.FinalReleaseComObject(books);
        }
    }

    /// <summary>A valid sheet name (at most 31 characters, none of <c>[]:*?/\</c>, no edge apostrophes) or null.</summary>
    static string? SheetName(string stem)
    {
        var name = new string(stem.Select(c => "[]:*?/\\".Contains(c) ? '_' : c).ToArray()).Trim('\'');
        if (name.Length > 31) name = name[..31];
        return name.Length == 0 ? null : name;
    }
}
