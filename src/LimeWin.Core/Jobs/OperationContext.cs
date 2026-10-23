namespace LimeWin.Core.Jobs;

/// <summary>Cancellation and progress for one running operation.</summary>
/// <param name="waiting">Told <c>true</c> when the operation starts waiting for an engine slot and <c>false</c> when it stops waiting.</param>
public sealed class OperationContext(CancellationToken token, IProgress<double>? progress = null, IProgress<bool>? waiting = null)
{
    public CancellationToken Token { get; } = token;

    readonly List<string> _created = [];

    /// <summary>Output files/folders registered via <see cref="Track"/>, in order.</summary>
    public IReadOnlyList<string> Created
    {
        get { lock (_created) return [.. _created]; }
    }

    /// <summary>Registers an output file or folder this operation has just created (reserved), so a failed run can remove it. Never track a path you did not create.</summary>
    public void Track(string path)
    {
        lock (_created) _created.Add(path);
    }

    /// <summary>Reports completion as a fraction, clamped to 0..1.</summary>
    public void Report(double fraction) => progress?.Report(Math.Clamp(fraction, 0.0, 1.0));

    /// <summary>
    /// Takes a slot of <paramref name="gate"/> (see <see cref="EngineGates"/>); dispose the result to give it back. When no
    /// slot is free the operation reports that it is waiting until it gets one or is cancelled (then nothing is taken).
    /// </summary>
    public async Task<IDisposable> EnterAsync(SemaphoreSlim gate)
    {
        Token.ThrowIfCancellationRequested();
        if (!gate.Wait(0))
        {
            waiting?.Report(true);
            try { await gate.WaitAsync(Token); }
            finally { waiting?.Report(false); }
        }
        return new Slot(gate);
    }

    /// <summary>Runs <paramref name="work"/> holding a slot of <paramref name="gate"/> (see <see cref="EnterAsync"/>).</summary>
    public async Task<T> InSlotAsync<T>(SemaphoreSlim gate, Func<Task<T>> work)
    {
        using (await EnterAsync(gate)) return await work();
    }

    sealed class Slot(SemaphoreSlim gate) : IDisposable
    {
        int _released;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0) gate.Release();
        }
    }
}
