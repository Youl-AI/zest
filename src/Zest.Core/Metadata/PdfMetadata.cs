using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;
using PdfSharp.Pdf.IO;
using Zest.Core.Jobs;
using Zest.Core.Output;

namespace Zest.Core.Metadata;

/// <summary>
/// The document Info of PDFs (PDFsharp 6.2): Title, Author, Subject, Keywords, Creator, Producer, plus the two dates read-only.
/// PDFsharp stamps its own Creator (when absent) and Producer on every save and has no switch for it, so the Info it writes is
/// replaced by an incremental update (a new revision appended to the saved file) carrying exactly what the user chose.
/// </summary>
internal static class PdfMetadata
{
    const string Unreadable = "PDF를 읽을 수 없습니다.";
    const string Encrypted = "암호로 보호된 PDF는 바꿀 수 없습니다.";

    static readonly string[] TextKeys = ["Title", "Author", "Subject", "Keywords", "Creator", "Producer"];
    static readonly string[] DateKeys = ["CreationDate", "ModDate"];

    /// <summary>An Info value that is a PDF name (e.g. <c>/Trapped /False</c>), kept as it was.</summary>
    sealed record NameValue(string Name);

    // ---- Read ----

    public static IReadOnlyList<MetadataTag> Read(string path, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        using var doc = Open(File.ReadAllBytes(path), PdfDocumentOpenMode.Import);
        var info = doc.Info;
        var rows = new List<MetadataTag>();
        foreach (var key in TextKeys)
        {
            var value = info.Elements.GetString("/" + key);
            if (string.IsNullOrWhiteSpace(value)) continue;
            var (group, label) = MetadataLabels.For("pdf", key);
            rows.Add(new($"pdf:{key}", group, label, value, Editable: true, Removable: true));
        }
        foreach (var key in DateKeys)
        {
            if (info.Elements.GetDateTime("/" + key, DateTime.MinValue) is var date && date == DateTime.MinValue) continue;
            var (group, label) = MetadataLabels.For("pdf", key);
            rows.Add(new($"pdf:{key}", group, label, date.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture), Editable: false, Removable: true));
        }
        return rows;
    }

    // ---- Write ----

    public static string Write(string source, MetadataEdit edit, OperationContext ctx)
    {
        ctx.Token.ThrowIfCancellationRequested();
        var bytes = File.ReadAllBytes(source);
        var entries = InfoEntries(bytes);
        Apply(entries, edit);
        using var doc = Open(bytes, PdfDocumentOpenMode.Modify);
        ctx.Report(0.3);

        // PDFsharp writes doc.Info and an XMP stream built from it into the body even though the appended revision replaces
        // them, so they must not keep the old values (they would stay in the file, unreferenced).
        foreach (var key in doc.Info.Elements.Keys.ToList())
            if (!entries.ContainsKey(key.TrimStart('/'))) doc.Info.Elements.Remove(key);
        foreach (var (key, value) in entries)
            if (value is string text) doc.Info.Elements.SetString("/" + key, text);
        // The catalog never references an XMP stream: the source's repeats the old Info and PDFsharp's own names PDFsharp.
        doc.Internals.Catalog.Elements.Remove("/Metadata");

        byte[] saved;
        using (var stream = new MemoryStream())
        {
            doc.Save(stream, closeStream: false);
            saved = stream.ToArray();
        }
        var result = WithInfo(saved, entries);
        ctx.Token.ThrowIfCancellationRequested();
        ctx.Report(0.8);

        var output = OutputNaming.ToolOutput(source, edit.RemoveAll ? "no metadata" : "edited", "pdf", ctx);
        try
        {
            File.WriteAllBytes(output, result);
        }
        catch
        {
            try { File.Delete(output); } catch (IOException) { }
            throw;
        }
        ctx.Report(1);
        return output;
    }

    /// <summary>The Info as it is now: text and names by key (without the slash); other value types are not carried.</summary>
    static Dictionary<string, object> InfoEntries(byte[] bytes)
    {
        // Read from an Import-mode copy: Modify mode replaces /ModDate with "now" while opening.
        using var doc = Open(bytes, PdfDocumentOpenMode.Import);
        var entries = new Dictionary<string, object>(StringComparer.Ordinal);
        foreach (var key in doc.Info.Elements.Keys)
        {
            var name = key.TrimStart('/');
            var item = doc.Info.Elements[key];
            if (item is PdfReference reference) item = reference.Value; // indirect values
            switch (item)
            {
                case PdfString s: entries[name] = s.Value; break;
                case PdfStringObject so: entries[name] = so.Value; break;
                case PdfName n: entries[name] = new NameValue(n.Value); break;
                case PdfDate d: entries[name] = d.Value.ToString("'D:'yyyyMMddHHmmss", CultureInfo.InvariantCulture) + OffsetOf(d.Value); break;
            }
        }
        return entries;
    }

    static string OffsetOf(DateTime date) => date.Kind == DateTimeKind.Utc ? "Z" : date.Kind == DateTimeKind.Local
        ? $"{(TimeZoneInfo.Local.GetUtcOffset(date) < TimeSpan.Zero ? '-' : '+')}{Math.Abs(TimeZoneInfo.Local.GetUtcOffset(date).Hours):D2}'{Math.Abs(TimeZoneInfo.Local.GetUtcOffset(date).Minutes):D2}'" : "";

    /// <summary>Applies <paramref name="edit"/> to <paramref name="entries"/>; true when a text or date entry changed.</summary>
    static bool Apply(Dictionary<string, object> entries, MetadataEdit edit)
    {
        var touched = false;
        if (edit.RemoveAll)
        {
            foreach (var key in entries.Keys.Where(k => k != "Trapped").ToList()) touched |= entries.Remove(key);
            return touched;
        }
        foreach (var (id, value) in edit.Changed)
        {
            if (!id.StartsWith("pdf:", StringComparison.Ordinal) || !TextKeys.Contains(id[4..])) continue; // dates are read-only
            var key = id[4..];
            if (string.IsNullOrWhiteSpace(value)) touched |= entries.Remove(key);
            else if (!(entries.TryGetValue(key, out var old) && old is string s && s == value)) { entries[key] = value; touched = true; }
        }
        foreach (var id in edit.Removed)
            if (id.StartsWith("pdf:", StringComparison.Ordinal) && (TextKeys.Contains(id[4..]) || DateKeys.Contains(id[4..])))
                touched |= entries.Remove(id[4..]);
        return touched;
    }

    /// <summary>
    /// Appends a revision to a PDFsharp-written file that replaces its Info object (same number) with <paramref name="entries"/>
    /// and its catalog with a copy that has no /Metadata (PDFsharp puts the entry back on every save).
    /// PDFsharp writes a classic xref table and an uncompressed trailer, which is all this needs to read back.
    /// </summary>
    static byte[] WithInfo(byte[] saved, Dictionary<string, object> entries)
    {
        var text = Encoding.Latin1.GetString(saved);
        var startxref = text.LastIndexOf("startxref", StringComparison.Ordinal);
        var trailerAt = text.LastIndexOf("trailer", startxref, StringComparison.Ordinal);
        if (startxref < 0 || trailerAt < 0) throw new ZestException(Unreadable);
        var dictEnd = text.LastIndexOf(">>", startxref, StringComparison.Ordinal);
        var trailer = text[(trailerAt + "trailer".Length)..(dictEnd + 2)].Trim();
        var previous = text[(startxref + "startxref".Length)..].Trim().Split(['\r', '\n'])[0];
        var info = Regex.Match(trailer, @"/Info\s+(\d+)\s+(\d+)\s+R");
        if (!info.Success || !trailer.EndsWith(">>", StringComparison.Ordinal) || !long.TryParse(previous, out _)) throw new ZestException(Unreadable);

        var body = new StringBuilder("<<");
        foreach (var (key, value) in entries)
            body.Append('/').Append(key).Append(value is NameValue n ? n.Name : Literal((string)value));
        body.Append(">>");

        // Objects of the new revision, in order: the Info and the catalog without its /Metadata entry.
        var root = Regex.Match(trailer, @"/Root\s+(\d+)\s+(\d+)\s+R");
        if (!root.Success) throw new ZestException(Unreadable);
        var catalog = Regex.Match(text, $@"(?<![0-9]){root.Groups[1].Value}\s+{root.Groups[2].Value}\s+obj\s*(<<.*?>>)\s*endobj", RegexOptions.Singleline);
        if (!catalog.Success) throw new ZestException(Unreadable);
        var withoutXmp = Regex.Replace(catalog.Groups[1].Value, @"/Metadata\s+\d+\s+\d+\s+R", "");
        var revision = new List<(string Number, string Generation, string Body)>
        {
            (info.Groups[1].Value, info.Groups[2].Value, body.ToString()),
            (root.Groups[1].Value, root.Groups[2].Value, withoutXmp),
        };

        using var result = new MemoryStream();
        result.Write(saved);
        var offsets = new List<long>();
        foreach (var (number, generation, content) in revision)
        {
            Append(result, "\n");
            offsets.Add(result.Position);
            Append(result, $"{number} {generation} obj\n{content}\nendobj");
        }
        Append(result, "\n");
        var xrefOffset = result.Position;
        var xref = new StringBuilder("xref\n");
        for (var i = 0; i < revision.Count; i++) // one subsection per object, each entry exactly 20 bytes
            xref.Append($"{revision[i].Number} 1\n{offsets[i]:D10} {int.Parse(revision[i].Generation, CultureInfo.InvariantCulture):D5} n \n");
        Append(result, $"{xref}trailer\n{trailer[..^2]}/Prev {previous}>>\nstartxref\n{xrefOffset}\n%%EOF\n");
        return result.ToArray();
    }

    static void Append(Stream stream, string text) => stream.Write(Encoding.Latin1.GetBytes(text));

    /// <summary>A PDF text string: plain ASCII in parentheses, anything else as UTF-16BE with a byte order mark.</summary>
    static string Literal(string value)
    {
        if (value.All(c => c is >= ' ' and <= '~'))
            return "(" + value.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)") + ")";
        return "<FEFF" + System.Convert.ToHexString(Encoding.BigEndianUnicode.GetBytes(value)) + ">";
    }

    static PdfDocument Open(byte[] bytes, PdfDocumentOpenMode mode)
    {
        var encrypted = false;
        try
        {
            using var stream = new MemoryStream(bytes);
            var doc = PdfReader.Open(stream, mode, args => { encrypted = true; args.Abort = true; }); // null when the user is asked and aborts
            if (doc is null) throw new ZestException(encrypted ? Encrypted : Unreadable);
            return doc;
        }
        catch (ZestException)
        {
            throw;
        }
        catch (Exception) when (encrypted)
        {
            throw new ZestException(Encrypted);
        }
        catch (Exception e) when (e is not (ZestException or OperationCanceledException))
        {
            throw new ZestException(Unreadable);
        }
    }
}
