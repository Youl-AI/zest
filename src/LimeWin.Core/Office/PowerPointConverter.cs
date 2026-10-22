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
            var output = OutputNaming.ConvertOutput(source, to);
            var saved = Path.Combine(work.Out, Path.GetFileName(output)); // the output's own name, as in WordConverter
            var format = to == "pdf" ? PowerPointFormat.Pdf : PowerPointFormat.Pptx;
            ctx.Track(output);
            await ppt.RunAsync(app =>
            {
                WithPresentation((object)app, input, from, pres =>
                {
                    try { pres.SaveAs(saved, format); }
                    catch (COMException e) { throw new LimeException($"변환 실패: {e.Message}"); }
                });
                return 0;
            }, Timeout, ctx.Token);
            ctx.Token.ThrowIfCancellationRequested();
            File.Move(saved, output);
            ctx.Report(1);
            return [output];
        }

        var filter = to == "jpg" ? "JPG" : "PNG";
        var count = await ppt.RunAsync(app =>
        {
            var n = 0;
            WithPresentation((object)app, input, from, pres => n = ExportSlides(pres, work.Out, to, filter, ctx));
            return n;
        }, Timeout, ctx.Token);
        ctx.Token.ThrowIfCancellationRequested();

        var stem = OutputNaming.Stem(source);
        string result;
        if (count == 1)
        {
            result = OutputNaming.ConvertOutput(source, to);
            ctx.Track(result);
            File.Move(SlideFile(work.Out, 1, to), result);
        }
        else
        {
            result = OutputNaming.CreatePagesFolder(source);
            ctx.Track(result);
            for (var i = 1; i <= count; i++)
                File.Move(SlideFile(work.Out, i, to), Path.Combine(result, OutputNaming.PageFileName(stem, i, count, to)));
        }
        ctx.Report(1);
        return [result];
    }

    static string SlideFile(string dir, int index1, string ext) => Path.Combine(dir, $"{index1}.{ext}");

    /// <summary>Runs on the Office STA thread: one PNG/JPG per slide into <paramref name="dir"/>; returns the slide count.</summary>
    static int ExportSlides(dynamic pres, string dir, string ext, string filter, OperationContext ctx)
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
                ctx.Token.ThrowIfCancellationRequested();
                dynamic slide = slides.Item(i);
                try { slide.Export(SlideFile(dir, i, ext), filter, ImageWidth, imageHeight); }
                catch (COMException e) { throw new LimeException($"변환 실패: {e.Message}"); }
                finally { Marshal.FinalReleaseComObject(slide); }
                ctx.Report(0.05 + 0.9 * i / count);
            }
            return count;
        }
        finally { Marshal.FinalReleaseComObject(slides); }
    }

    /// <summary>
    /// Runs on the Office STA thread: opens the private copy read-only and without a window, refuses macros, runs
    /// <paramref name="use"/>, and closes only that presentation.
    /// </summary>
    static void WithPresentation(dynamic app, string input, string from, Action<dynamic> use)
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

            if ((bool)pres!.HasVBProject) throw new LimeException("매크로가 들어 있는 문서는 변환하지 않습니다.");
            use(pres);
        }
        finally
        {
            if (pres is not null)
            {
                try { pres.Close(); }
                catch (COMException) { }
                Marshal.FinalReleaseComObject(pres);
            }
            Marshal.FinalReleaseComObject(presentations);
        }
    }
}
