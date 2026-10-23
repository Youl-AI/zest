using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using LimeWin.Core.Catalog;
using LimeWin.Core.Convert;
using LimeWin.Core.Jobs;
using LimeWin.Core.Output;

namespace LimeWin.Core.Office;

/// <summary>
/// Document conversion through Word (docx doc rtf odt txt html md). md↔html never starts Word.
/// PDF is not a source: a hidden Word never finishes opening a PDF on the target machine (Task 8).
/// Word reads (a copy of the source) and writes only inside a private temp folder; finished files are then moved next to the source.
/// </summary>
public sealed class WordConverter(OfficeHost word) : IConverter
{
    static readonly TimeSpan Timeout = TimeSpan.FromMinutes(2); // spec §7
    static readonly HashSet<string> Sources = ["docx", "doc", "rtf", "odt", "txt", "html", "md"];
    static readonly HashSet<string> Targets = ["docx", "odt", "txt", "md", "html", "pdf"];
    static readonly UTF8Encoding Utf8 = new(false);

    public bool Handles(string from, string to) => Sources.Contains(from) && Targets.Contains(to) && from != to;

    public async Task<IReadOnlyList<string>> ConvertAsync(string source, string to, OperationContext ctx)
    {
        ctx.Token.ThrowIfCancellationRequested();
        var from = FormatCatalog.FormatOf(source) ?? "";

        if ((from, to) is ("md", "html"))
        {
            var html = MarkdownBridge.ToHtmlDocument(await ReadSourceAsync(source, ctx.Token), OutputNaming.Stem(source));
            var output = OutputNaming.ConvertOutput(source, to, ctx);
            await File.WriteAllTextAsync(output, html, Utf8, ctx.Token);
            return [output];
        }
        if ((from, to) is ("html", "md"))
        {
            var md = MarkdownBridge.FromHtml(await ReadSourceAsync(source, ctx.Token));
            var output = OutputNaming.ConvertOutput(source, to, ctx);
            await File.WriteAllTextAsync(output, md, Utf8, ctx.Token);
            return [output];
        }

        OfficeWorkFolder.EnsureNotEmpty(source);
        using (var work = OfficeWorkFolder.Create())
        {
            // Word only ever opens a private copy (Documents.Open defaults to Revert:=False and would return the user's own Document).
            string input;
            if (from == "md")
            {
                input = work.NewInputPath(".html");
                var md = await ReadSourceAsync(source, ctx.Token);
                await File.WriteAllTextAsync(input, MarkdownBridge.ToHtmlDocument(md, OutputNaming.Stem(source)), Utf8, ctx.Token);
            }
            else input = work.CopySource(source, Path.GetExtension(source));

            // Word saves under the output's own (reserved) name so HTML image folders ("<stem>_files") keep matching links.
            var output = OutputNaming.ConvertOutput(source, to, ctx);
            var outDir = work.Out;
            var stem = OutputNaming.Stem(output);
            var saved = Path.Combine(outDir, to switch { "md" => stem + ".html", _ => stem + "." + to });
            (int Format, object Encoding) save = to switch
            {
                "docx" => (WordFormat.Docx, Missing.Value),
                "odt" => (WordFormat.Odt, Missing.Value),
                "pdf" => (WordFormat.Pdf, Missing.Value),
                "txt" => (WordFormat.UnicodeText, MsoConst.EncodingUnicodeLittleEndian),
                "html" or "md" => (WordFormat.FilteredHtml, MsoConst.EncodingUtf8),
                _ => throw new LimeException("이 변환은 지원하지 않습니다."),
            };

            ctx.Report(0.05);
            await word.RunAsync(app => { SaveWithWord(app, input, saved, save.Format, save.Encoding); return 0; }, Timeout, ctx.Token);
            ctx.Token.ThrowIfCancellationRequested();
            ctx.Report(0.9);

            await PlaceCompanionFoldersAsync(outDir, Path.GetDirectoryName(output)!, ctx);
            switch (to)
            {
                case "txt": // UTF-16 from Word, written back as UTF-8 without BOM
                    await File.WriteAllTextAsync(output, await File.ReadAllTextAsync(saved, Encoding.Unicode, ctx.Token), Utf8, ctx.Token);
                    break;
                case "md":
                    var html = await File.ReadAllTextAsync(saved, Encoding.UTF8, ctx.Token);
                    await File.WriteAllTextAsync(output, MarkdownBridge.FromHtml(html), Utf8, ctx.Token);
                    break;
                default:
                    File.Move(saved, output, overwrite: true); // onto our own placeholder only
                    break;
            }
            ctx.Report(1);
            return [output];
        }
    }

    /// <summary>Runs on the Office STA thread.</summary>
    static void SaveWithWord(dynamic app, string input, string saved, int format, object encoding)
    {
        dynamic docs = app.Documents;
        dynamic? doc = null;
        try
        {
            try
            {
                doc = docs.Open(FileName: input, ConfirmConversions: false, ReadOnly: true, AddToRecentFiles: false,
                    PasswordDocument: WordConst.WrongPassword, Visible: false, NoEncodingDialog: true);
            }
            catch (COMException e) when ((e.ErrorCode & 0xFFFF) == WordConst.PasswordIncorrectError)
            {
                throw new LimeException("암호로 보호된 문서는 열 수 없습니다.");
            }
            catch (COMException)
            {
                throw new LimeException("문서를 열 수 없습니다.");
            }

            if ((bool)doc.HasVBProject) throw new LimeException("매크로가 들어 있는 문서는 변환하지 않습니다.");

            if (format == WordFormat.FilteredHtml)
            {
                // HTML ignores SaveAs2's Encoding and follows the document's web options (else the ANSI code page).
                dynamic web = doc.WebOptions;
                try { web.Encoding = MsoConst.EncodingUtf8; }
                finally { Marshal.FinalReleaseComObject(web); }
            }
            try { doc.SaveAs2(FileName: saved, FileFormat: format, Encoding: encoding); }
            catch (COMException e) { throw new LimeException($"변환 실패: {e.Message}"); }
        }
        finally
        {
            if (doc is not null)
            {
                try { doc.Close(SaveChanges: WordConst.DoNotSaveChanges); }
                catch (COMException) { }
                Marshal.FinalReleaseComObject(doc);
            }
            Marshal.FinalReleaseComObject(docs);
        }
    }

    /// <summary>Copies the image folders Word wrote beside an HTML file next to the output, each into a folder created (and tracked) by us.</summary>
    static async Task PlaceCompanionFoldersAsync(string outDir, string destination, OperationContext ctx)
    {
        foreach (var folder in Directory.GetDirectories(outDir))
        {
            var target = Path.Combine(destination, Path.GetFileName(folder));
            if (!OutputNaming.TryCreateFolder(target))
                throw new LimeException("같은 이름의 폴더가 있어 그림을 저장할 수 없습니다.");
            ctx.Track(target);
            await Task.Run(() => CopyDirectory(folder, target), ctx.Token);
        }
    }

    /// <summary>Copies a folder tree into <paramref name="to"/> (which may already exist, empty); never overwrites a file.</summary>
    static void CopyDirectory(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var f in Directory.GetFiles(from)) File.Copy(f, Path.Combine(to, Path.GetFileName(f)));
        foreach (var d in Directory.GetDirectories(from)) CopyDirectory(d, Path.Combine(to, Path.GetFileName(d)));
    }

    static async Task<string> ReadSourceAsync(string source, CancellationToken ct)
    {
        try { return await File.ReadAllTextAsync(source, Encoding.UTF8, ct); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw new LimeException("원본 파일을 열 수 없습니다.");
        }
    }
}
