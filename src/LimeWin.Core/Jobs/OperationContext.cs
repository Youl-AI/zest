namespace LimeWin.Core.Jobs;

/// <summary>Cancellation and progress for one running operation.</summary>
public sealed class OperationContext(CancellationToken token, IProgress<double>? progress = null)
{
    public CancellationToken Token { get; } = token;

    readonly List<string> _created = [];

    /// <summary>Output files/folders registered via <see cref="Track"/>, in order.</summary>
    public IReadOnlyList<string> Created
    {
        get { lock (_created) return [.. _created]; }
    }

    /// <summary>Registers an output file or folder the converter is about to create, so a failed run can remove it.</summary>
    public void Track(string path)
    {
        lock (_created) _created.Add(path);
    }

    /// <summary>Reports completion as a fraction, clamped to 0..1.</summary>
    public void Report(double fraction) => progress?.Report(Math.Clamp(fraction, 0.0, 1.0));
}
