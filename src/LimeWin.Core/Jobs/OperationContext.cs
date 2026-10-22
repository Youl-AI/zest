namespace LimeWin.Core.Jobs;

/// <summary>Cancellation and progress for one running operation.</summary>
public sealed class OperationContext(CancellationToken token, IProgress<double>? progress = null)
{
    public CancellationToken Token { get; } = token;

    /// <summary>Reports completion as a fraction, clamped to 0..1.</summary>
    public void Report(double fraction) => progress?.Report(Math.Clamp(fraction, 0.0, 1.0));
}
