namespace Zest.Core.Jobs;

/// <summary>
/// How many jobs may use each engine at once, so dropping many files cannot start dozens of encoders together. A job takes
/// its slot inside the job (<see cref="OperationContext.EnterAsync"/>), so a queued job shows "대기 중" and can be cancelled.
/// PDF rendering (PDFium) and Office are already one at a time (<c>PdfRenderer</c>, <c>OfficeHost</c>), and background
/// removal has its own one-image gate.
/// </summary>
public static class EngineGates
{
    /// <summary>ffmpeg processes at once: each one already uses several cores.</summary>
    public const int FfmpegSlots = 2;

    /// <summary>In-process image work (Magick decode/encode, compress, crop, images to PDF) at once: half the logical cores, at least one.</summary>
    public static int ImageSlots { get; } = Math.Max(1, Environment.ProcessorCount / 2);

    public static SemaphoreSlim Ffmpeg { get; } = new(FfmpegSlots, FfmpegSlots);

    public static SemaphoreSlim Image { get; } = new(ImageSlots, ImageSlots);
}
