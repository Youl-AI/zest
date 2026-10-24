using Zest.Core.Jobs;

namespace Zest.Core.Tests;

/// <summary>Jobs take turns on each engine; a job waiting for its turn says so and can still be cancelled (final review I2).</summary>
public class EngineGateTests
{
    /// <summary>Records reports synchronously, in order.</summary>
    sealed class Recorder : IProgress<bool>
    {
        public readonly List<bool> Seen = [];
        public void Report(bool value) { lock (Seen) Seen.Add(value); }
    }

    [Fact]
    public void Slots_FfmpegTwo_ImageHalfTheCores()
    {
        // The slot counts, not CurrentCount: other test classes may be using the shared gates right now.
        Assert.Equal(2, EngineGates.FfmpegSlots);
        Assert.Equal(Math.Max(1, Environment.ProcessorCount / 2), EngineGates.ImageSlots);
    }

    [Fact]
    public async Task FreeSlot_EntersAtOnce_WithoutWaitingReport()
    {
        var gate = new SemaphoreSlim(1, 1);
        var waiting = new Recorder();
        var ctx = new OperationContext(CancellationToken.None, null, waiting);
        using (await ctx.EnterAsync(gate))
            Assert.Equal(0, gate.CurrentCount);
        Assert.Equal(1, gate.CurrentCount);
        Assert.Empty(waiting.Seen);
    }

    [Fact]
    public async Task FullGate_ReportsWaiting_ThenRunning_WhenASlotFrees()
    {
        var gate = new SemaphoreSlim(1, 1);
        var first = await new OperationContext(CancellationToken.None).EnterAsync(gate);
        var waiting = new Recorder();
        var entering = new OperationContext(CancellationToken.None, null, waiting).EnterAsync(gate);

        Assert.False(entering.IsCompleted);
        Assert.Equal([true], waiting.Seen);

        first.Dispose();
        using (await entering.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken))
            Assert.Equal([true, false], waiting.Seen);
        Assert.Equal(1, gate.CurrentCount);
    }

    [Fact]
    public async Task CancelWhileWaiting_ThrowsAndTakesNoSlot()
    {
        var gate = new SemaphoreSlim(1, 1);
        var first = await new OperationContext(CancellationToken.None).EnterAsync(gate);
        using var cts = new CancellationTokenSource();
        var waiting = new Recorder();
        var entering = new OperationContext(cts.Token, null, waiting).EnterAsync(gate);

        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => entering);
        Assert.Equal([true, false], waiting.Seen);

        first.Dispose();
        Assert.Equal(1, gate.CurrentCount);
    }

    /// <summary>Takes every slot of a shared gate (waiting for other tests to give theirs back) until disposed.</summary>
    static async Task<Action> HoldAll(SemaphoreSlim gate, int slots)
    {
        for (var i = 0; i < slots; i++) await gate.WaitAsync(TestContext.Current.CancellationToken);
        return () => gate.Release(slots);
    }

    static async Task WaitUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (!condition() && DateTime.UtcNow < deadline) await Task.Delay(20, TestContext.Current.CancellationToken);
        Assert.True(condition());
    }

    /// <summary>Runs a job while every slot of its engine is taken: it must wait (saying so) and finish once the slots free up.</summary>
    static async Task AssertWaitsForSlot(SemaphoreSlim gate, int slots, Func<OperationContext, Task> job)
    {
        var waiting = new Recorder();
        var release = await HoldAll(gate, slots);
        Task running;
        try
        {
            running = Task.Run(() => job(new OperationContext(CancellationToken.None, null, waiting)));
            await WaitUntil(() => { lock (waiting.Seen) return waiting.Seen.Count > 0; });
            await Task.Delay(100, TestContext.Current.CancellationToken);
            Assert.False(running.IsCompleted);
        }
        finally { release(); }
        await running.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
        Assert.Equal([true, false], waiting.Seen);
    }

    [Fact]
    public async Task ImageConversion_WaitsForAnImageSlot()
    {
        var dir = TestFiles.TempDir();
        var src = Fixtures.FixtureFactory.Image(dir, "a.png");
        await AssertWaitsForSlot(EngineGates.Image, EngineGates.ImageSlots,
            ctx => new Convert.ConversionRouter([new Convert.ImageConverter()]).ConvertAsync(src, "jpg", ctx));
        Assert.True(File.Exists(Path.Combine(dir, "a.jpg")));
    }

    [Fact]
    public async Task Compress_WaitsForAnImageSlot()
    {
        var dir = TestFiles.TempDir();
        var src = Fixtures.FixtureFactory.NoiseImage(dir, "a.jpg", 200, 200);
        await AssertWaitsForSlot(EngineGates.Image, EngineGates.ImageSlots,
            ctx => Tools.ImageCompressor.CompressAsync(src, new Tools.CompressOptions(), ctx));
    }

    [Fact]
    public async Task SplitPdf_WaitsForAnImageSlot()
    {
        var dir = TestFiles.TempDir();
        var src = Fixtures.FixtureFactory.Pdf(dir, "a.pdf", 2);
        await AssertWaitsForSlot(EngineGates.Image, EngineGates.ImageSlots, ctx => Tools.PdfTools.SplitAsync(src, ctx));
    }

    [Fact, Trait("Category", "Ffmpeg")]
    public async Task FfmpegConversion_WaitsForAnFfmpegSlot()
    {
        var ffmpeg = Convert.FfmpegLocator.Locate() ?? throw new InvalidOperationException("ffmpeg is not on PATH.");
        var dir = TestFiles.TempDir();
        var src = Fixtures.FixtureFactory.Media(dir, "a.wav", seconds: 1);
        await AssertWaitsForSlot(EngineGates.Ffmpeg, EngineGates.FfmpegSlots,
            ctx => new Convert.ConversionRouter([new Convert.FfmpegConverter(ffmpeg)]).ConvertAsync(src, "mp3", ctx));
    }

    [Fact]
    public async Task ReleasingTwice_ReleasesOnce()
    {
        var gate = new SemaphoreSlim(2, 2);
        var slot = await new OperationContext(CancellationToken.None).EnterAsync(gate);
        slot.Dispose();
        slot.Dispose();
        Assert.Equal(2, gate.CurrentCount);
    }
}
