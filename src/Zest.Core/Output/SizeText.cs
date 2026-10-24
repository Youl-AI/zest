namespace Zest.Core.Output;

/// <summary>File sizes for people: decimal units, like the compress window's target field.</summary>
public static class SizeText
{
    /// <summary>"182 KB" / "1.4 MB" with decimal units like the target field.</summary>
    public static string Format(long bytes) => bytes switch
    {
        < 1_000 => $"{bytes} B",
        < 1_000_000 => $"{Math.Round(bytes / 1_000.0):0} KB",
        _ => $"{bytes / 1_000_000.0:0.#} MB",
    };
}
