using System.Runtime.InteropServices;
using LimeWin.Core.Catalog;
using LimeWin.Core.Convert;
using LimeWin.Core.Jobs;
using LimeWin.Core.Output;

namespace LimeWin.Core.Office;

/// <summary>
/// Slides through PowerPoint (ppt pptx odp to pdf, jpg, png; ppt to pptx). PowerPoint is single-instance and cannot be
/// hidden, so presentations are opened without a window, never <c>Visible</c>; in a PowerPoint the user has open only our
/// presentation is closed (spec §7). jpg/png are one 1920 px wide image per slide: one file for a single slide, else a
/// "(pages)" folder. PowerPoint reads a private copy of the source and writes only inside a private temp folder.
/// </summary>
public sealed class PowerPointConverter(OfficeHost ppt) : IConverter
{
    static readonly TimeSpan Timeout = TimeSpan.FromMinutes(2); // spec §7
    const int ImageWidth = 1920;
    static readonly HashSet<string> Sources = ["ppt", "pptx", "odp"];

    public bool Handles(string from, string to) =>
        Sources.Contains(from) && (to is "pdf" or "jpg" or "png" || (from, to) is ("ppt", "pptx"));

    public async Task<IReadOnlyList<string>> ConvertAsync(string source, string to, OperationContext ctx)
    {
        ctx.Token.ThrowIfCancellationRequested();
        var from = FormatCatalog.FormatOf(source) ?? "";
        OfficeWorkFolder.EnsureNotEmpty(source);
        using var work = OfficeWorkFolder.Create();
        var input = work.CopySource(source, Path.GetExtension(source));
        ctx.Report(0.05);

        if (to is "pdf" or "pptx")
        {
            var output = OutputNaming.ConvertOutput(source, to, ctx);
            var saved = Path.Combine(work.Out, Path.GetFileName(output)); // the output's own name, as in WordConverter
            var format = to == "pdf" ? PowerPointFormat.Pdf : PowerPointFormat.Pptx;
            await ppt.RunAsync(app =>
            {
                WithPresentation((object)app, input, saved, from, pres => pres.SaveAs(saved, format));
                return 0;
            }, Timeout, ctx.Token);
            ctx.Token.ThrowIfCancellationRequested();
            File.Move(saved, output, overwrite: true); // onto our own placeholder only
            ctx.Report(1);
            return [output];
        }

        var filter = to == "jpg" ? "JPG" : "PNG";
        // Once RunAsync has given up (timeout, cancel), an item still running on an abandoned thread stops at the next
        // slide and reports nothing more.
        int count;
        using (var stop = CancellationTokenSource.CreateLinkedTokenSource(ctx.Token))
        {
            var stopToken = stop.Token; // read now: the item may run on after the source is disposed
            try
            {
                count = await ppt.RunAsync(app =>
                {
                    var n = 0;
                    WithPresentation((object)app, input, null, from, pres => n = ExportSlides(pres, work.Out, to, filter, stopToken, ctx));
                    return n;
                }, Timeout, ctx.Token);
            }
            finally { stop.Cancel(); }
        }
        ctx.Token.ThrowIfCancellationRequested();

        var stem = OutputNaming.Stem(source);
        string result;
        if (count == 1)
        {
            result = OutputNaming.ConvertOutput(source, to, ctx);
            File.Move(SlideFile(work.Out, 1, to), result, overwrite: true); // onto our own placeholder only
        }
        else
        {
            result = OutputNaming.CreatePagesFolder(source, ctx);
            for (var i = 1; i <= count; i++)
                File.Move(SlideFile(work.Out, i, to), Path.Combine(result, OutputNaming.PageFileName(stem, i, count, to)));
        }
        ctx.Report(1);
        return [result];
    }

    static string SlideFile(string dir, int index1, string ext) => Path.Combine(dir, $"{index1}.{ext}");

    /// <summary>Runs on the Office STA thread: one PNG/JPG per slide into <paramref name="dir"/>; returns the slide count.</summary>
    static int ExportSlides(dynamic pres, string dir, string ext, string filter, CancellationToken stop, OperationContext ctx)
    {
        dynamic setup = pres.PageSetup;
        double width = setup.SlideWidth, height = setup.SlideHeight;
        Marshal.FinalReleaseComObject(setup);
        var imageHeight = (int)Math.Round(ImageWidth * height / width);

        dynamic slides = pres.Slides;
        try
        {
            int count = slides.Count;
            if (count == 0) throw new LimeException("슬라이드가 없는 프레젠테이션입니다.");
            for (var i = 1; i <= count; i++)
            {
                stop.ThrowIfCancellationRequested();
                dynamic slide = slides.Item(i);
                try { slide.Export(SlideFile(dir, i, ext), filter, ImageWidth, imageHeight); }
                finally { Marshal.FinalReleaseComObject(slide); }
                if (!stop.IsCancellationRequested) ctx.Report(0.05 + 0.9 * i / count);
            }
            return count;
        }
        finally { Marshal.FinalReleaseComObject(slides); }
    }

    /// <summary>
    /// Runs on the Office STA thread: opens the private copy read-only and without a window, refuses macros, runs
    /// <paramref name="use"/>, and closes only that presentation. COM failures after opening become "변환 실패: …".
    /// </summary>
    static void WithPresentation(dynamic app, string input, string? savedAs, string from, Action<dynamic> use)
    {
        dynamic presentations = app.Presentations;
        dynamic? pres = null;
        try
        {
            try
            {
                // "path::password::" with a wrong password: a protected file fails at once instead of prompting.
                pres = presentations.Open(FileName: input + PowerPointConst.WrongPasswordSuffix, ReadOnly: MsoConst.True,
                    Untitled: MsoConst.False, WithWindow: MsoConst.False);
            }
            catch (COMException e) when (OfficeEncryption.IsEncryptedPackage(input)
                || (from == "ppt" && e.HResult == PowerPointConst.PasswordIncorrectHResult))
            {
                throw new LimeException("암호로 보호된 문서는 열 수 없습니다.");
            }
            catch (COMException)
            {
                throw new LimeException("문서를 열 수 없습니다.");
            }

            try
            {
                if ((bool)pres!.HasVBProject) throw new LimeException("매크로가 들어 있는 문서는 변환하지 않습니다.");
                use(pres);
            }
            catch (COMException e) { throw new LimeException($"변환 실패: {e.Message}"); }
        }
        finally
        {
            if (pres is not null)
            {
                try { pres.Close(); }
                catch (COMException) { CloseOurCopy(presentations, input, savedAs); }
                Marshal.FinalReleaseComObject(pres);
            }
            Marshal.FinalReleaseComObject(presentations);
        }
    }

    /// <summary>Best effort: closes any open presentation that is our private copy (by its path before or after SaveAs).</summary>
    static void CloseOurCopy(dynamic presentations, string input, string? savedAs)
    {
        try
        {
            foreach (dynamic p in presentations)
            {
                try
                {
                    if (OfficeWorkFolder.IsOurCopy((string)p.FullName, input, savedAs)) p.Close();
                }
                catch (COMException) { }
                finally { Marshal.FinalReleaseComObject(p); }
            }
        }
        catch (COMException) { }
    }
}
