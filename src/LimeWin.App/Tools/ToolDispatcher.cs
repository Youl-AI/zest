using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LimeWin.App.Actions;
using LimeWin.Core;
using LimeWin.Core.Catalog;
using LimeWin.Core.Jobs;
using LimeWin.Core.Tools;

namespace LimeWin.App.Tools;

/// <summary>
/// Turns a drop on a tool petal into either a tool window (compress, crop, order) or jobs started at once (split, a single
/// image to PDF). Every tool job runs through <see cref="Jobs.JobRunner"/> with a Korean title. Call on the UI thread.
/// </summary>
public static class ToolDispatcher
{
    /// <param name="dropAt">Drop point in physical screen pixels; the window opens centred on that monitor (cursor position when null).</param>
    public static void Handle(LimeTool tool, IReadOnlyList<string> files, Point? dropAt = null)
    {
        if (files.Count == 0) return;
        var at = dropAt is { } p ? new Native.POINT { X = (int)p.X, Y = (int)p.Y } : Native.CursorPos();
        switch (tool)
        {
            case LimeTool.Compress:
                var sources = files.Select(f => new CompressSource(f, LengthOrNull(f))).ToList();
                Present(new CompressOptionsWindow(sources, options => Compress(files, options)), at);
                break;

            case LimeTool.Crop:
                _ = OpenCropAsync(files[0], at);
                break;

            case LimeTool.ImagesToPdf when files.Count == 1:
                ImagesToPdf(files); // nothing to order
                break;

            case LimeTool.ImagesToPdf:
                OpenOrder(OrderMode.ImagesToPdf, files, ImagesToPdf, at);
                break;

            case LimeTool.MergePdfs:
                OpenOrder(OrderMode.MergePdfs, files, Merge, at);
                break;

            case LimeTool.SplitPdf:
                foreach (var pdf in files)
                    Run($"{Name(pdf)} 나누기", ctx => PdfTools.SplitAsync(pdf, ctx));
                break;

            case LimeTool.CopyText:
                CopyText(files, at);
                break;

            // Not built yet: Task 16 fills RemoveBackground.
            case LimeTool.RemoveBackground:
            default:
                Services.Jobs.Run("배경 제거", _ => throw new LimeException("준비 중입니다."));
                break;
        }
    }

    /// <summary>Reads every file in one job ("글자 인식 중"), then copies the text and opens the result window on the UI thread.</summary>
    static void CopyText(IReadOnlyList<string> files, Native.POINT at)
    {
        var dispatcher = Application.Current.Dispatcher;
        Services.Jobs.Run($"{Several(files, "개")} 글자 인식 중", async ctx =>
        {
            var results = new List<(string Path, string Text)>();
            for (var i = 0; i < files.Count; i++)
            {
                ctx.Token.ThrowIfCancellationRequested();
                var (done, count) = (i, files.Count);
                var each = new OperationContext(ctx.Token, new Relay(f => ctx.Report((done + f) / count)));
                results.Add((files[i], await OcrService.RecognizeAsync(files[i], each)));
            }
            var text = OcrService.Combine(results);
            await dispatcher.InvokeAsync(() => ShowText(files, text, at));
            return (IReadOnlyList<string>)[];
        });
    }

    static void ShowText(IReadOnlyList<string> files, string text, Native.POINT at)
    {
        var copied = false;
        if (text.Length > 0)
        {
            var data = new DataObject();
            data.SetText(text);
            copied = ActionRunner.TrySetClipboard(data);
        }
        var window = new TextResultWindow(files, text, copied,
            edited => Run($"{Name(files[0])} 텍스트 저장", ctx => OcrService.SaveTextAsync(files[0], edited, ctx)));
        Present(window, at);
    }

    /// <summary>Progress reported on the calling thread (the job's own progress already marshals to the UI).</summary>
    sealed class Relay(Action<double> report) : IProgress<double>
    {
        public void Report(double value) => report(value);
    }

    static void Compress(IReadOnlyList<string> files, CompressOptions options)
    {
        foreach (var file in files)
            Run($"{Name(file)} 압축", ctx => ImageCompressor.CompressAsync(file, options, ctx));
    }

    static async Task OpenCropAsync(string file, Native.POINT at)
    {
        // Decode off the UI thread at the size the preview will be shown on that monitor; the window opens when it is ready.
        var edge = (int)Math.Ceiling(CropWindow.PreviewMaxLongEdge * Native.ScaleAt(at.X, at.Y));
        PreviewImage preview;
        BitmapSource bitmap;
        try
        {
            (preview, bitmap) = await Task.Run(() =>
            {
                var p = ImagePreview.Load(file, edge, singleFrameOnly: true); // fails exactly where the cropper would
                return (p, ToBitmap(p));
            });
        }
        catch (Exception e)
        {
            FailedCard($"{Name(file)} 자르기", e);
            return;
        }
        try
        {
            var window = new CropWindow(file, bitmap, preview.SourceWidth, preview.SourceHeight,
                rect => Run($"{Name(file)} 자르기", ctx => ImageCropper.CropAsync(file, rect, ctx)));
            Present(window, at);
        }
        catch (Exception e)
        {
            // This method's task is discarded, so nothing after the decode may throw unseen.
            FailedCard($"{Name(file)} 자르기", e);
        }
    }

    /// <summary>Shows an error that happened outside a job on a failed card, like a job failure.</summary>
    static void FailedCard(string title, Exception e) =>
        Services.Jobs.Run(title, _ => Task.FromException<IReadOnlyList<string>>(e));

    static void OpenOrder(OrderMode mode, IReadOnlyList<string> files, Action<IReadOnlyList<string>> go, Native.POINT at)
    {
        var window = new OrderWindow(mode, OrderWindow.ItemsFor(files, mode), go);
        Present(window, at);
        window.LoadDetailsInBackground(Native.ScaleAt(at.X, at.Y));
    }

    static void ImagesToPdf(IReadOnlyList<string> images) =>
        Run($"{Several(images, "장")} PDF 만들기", ctx => PdfTools.ImagesToPdfAsync(images, ctx));

    static void Merge(IReadOnlyList<string> pdfs) =>
        Run($"{Several(pdfs, "개")} 합치기", ctx => PdfTools.MergeAsync(pdfs, ctx));

    static void Run(string title, Func<Core.Jobs.OperationContext, Task<string>> work) =>
        Services.Jobs.Run(title, async ctx => (IReadOnlyList<string>)[await work(ctx)]);

    /// <summary>
    /// Centres the window on the drop monitor's work area (placed before it is shown, so it never flashes elsewhere),
    /// shows it, brings it to the front (see <see cref="Native.BringToFront"/>) and lets Esc close it.
    /// </summary>
    static void Present(Window window, Native.POINT at)
    {
        // Bubbling KeyDown, not Preview: an open combo dropdown handles its own Esc first and only closes itself.
        window.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            e.Handled = true;
            window.Close();
        };
        // SizeToContent is applied only during Show, so the handle would be created (and centred) at the default height.
        // Measure the content first and start at the final height; SizeToContent still follows later changes.
        if (window.SizeToContent == SizeToContent.Height && window.Content is FrameworkElement content)
        {
            content.Measure(new Size(window.Width, double.PositiveInfinity));
            window.Height = Math.Ceiling(content.DesiredSize.Height);
        }
        Native.CenterInWorkArea(window, at.X, at.Y);
        window.Show();
        Native.BringToFront(window);
    }

    /// <summary>A frozen WPF bitmap of a preview (safe to hand from a worker thread to the UI thread).</summary>
    internal static BitmapSource ToBitmap(PreviewImage p)
    {
        var bitmap = BitmapSource.Create(p.Width, p.Height, 96, 96, PixelFormats.Bgra32, null, p.Bgra, p.Width * 4);
        bitmap.Freeze();
        return bitmap;
    }

    static string Name(string path) => Path.GetFileName(path);

    /// <summary>"사진.jpg" or "사진.jpg 외 2장".</summary>
    static string Several(IReadOnlyList<string> files, string counter) =>
        files.Count == 1 ? Name(files[0]) : $"{Name(files[0])} 외 {files.Count - 1}{counter}";

    static long? LengthOrNull(string path)
    {
        try
        {
            return new FileInfo(path).Length;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
