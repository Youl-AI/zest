using ImageMagick;
using LimeWin.Core.Convert;
using LimeWin.Core.Jobs;
using LimeWin.Core.Output;

namespace LimeWin.Core.Tests;

/// <summary>
/// Output names are claimed atomically where they are chosen, so jobs running side by side never share, overwrite or
/// clean up each other's outputs (final review C1).
/// </summary>
public class OutputReservationTests : IDisposable
{
    readonly string Dir = TestFiles.TempDir();

    public void Dispose() { try { Directory.Delete(Dir, true); } catch (IOException) { } }

    static string Solid(string dir, string name, MagickColor color)
    {
        var path = Path.Combine(dir, name);
        using var image = new MagickImage(color, 800, 600);
        image.Write(path);
        return path;
    }

    static string CenterColor(string path)
    {
        using var image = new MagickImage(path);
        using var pixels = image.GetPixels();
        var c = pixels.GetPixel(400, 300).ToColor()!;
        return c.R > c.B ? "red" : "blue";
    }

    [Fact]
    public async Task ConcurrentSameStemConversions_GetDistinctOutputs()
    {
        for (var round = 0; round < 5; round++)
        {
            var dir = Directory.CreateDirectory(Path.Combine(Dir, $"round{round}")).FullName;
            var png = Solid(dir, "a.png", MagickColors.Red);
            var jpg = Solid(dir, "a.jpg", MagickColors.Blue);
            var router = new ConversionRouter([new ImageConverter()]);

            var results = await Task.WhenAll(
                Task.Run(() => router.ConvertAsync(png, "webp", new OperationContext(CancellationToken.None))),
                Task.Run(() => router.ConvertAsync(jpg, "webp", new OperationContext(CancellationToken.None))));

            var outputs = results.Select(r => Assert.Single(r)).ToList();
            Assert.Equal(["a 2.webp", "a.webp"], outputs.Select(Path.GetFileName).Order());
            Assert.Equal("red", CenterColor(outputs[0]));
            Assert.Equal("blue", CenterColor(outputs[1]));
        }
    }

    /// <summary>Plays a writer that picks its name, waits (as an Office job does while Office works) and then fails.</summary>
    sealed class ReserveThenFail(TaskCompletionSource reserved, Task proceed) : IConverter
    {
        public bool Handles(string from, string to) => true;

        public async Task<IReadOnlyList<string>> ConvertAsync(string source, string to, OperationContext ctx)
        {
            OutputNaming.ConvertOutput(source, to, ctx);
            reserved.SetResult();
            await proceed;
            throw new LimeException("실패");
        }
    }

    [Fact]
    public async Task FailingJob_NeverDeletesSiblingOutput()
    {
        var png = Solid(Dir, "a.png", MagickColors.Red);
        var jpg = Solid(Dir, "a.jpg", MagickColors.Blue);
        var reserved = new TaskCompletionSource();
        var proceed = new TaskCompletionSource();

        var failing = new ConversionRouter([new ReserveThenFail(reserved, proceed.Task)])
            .ConvertAsync(jpg, "webp", new OperationContext(CancellationToken.None));
        await reserved.Task;
        var sibling = await new ConversionRouter([new ImageConverter()])
            .ConvertAsync(png, "webp", new OperationContext(CancellationToken.None));
        proceed.SetResult();
        await Assert.ThrowsAsync<LimeException>(() => failing);

        var output = Assert.Single(sibling);
        Assert.True(File.Exists(output));
        Assert.Equal("red", CenterColor(output));
        Assert.Equal(new[] { "a.jpg", "a.png", Path.GetFileName(output) }.Order(), Directory.GetFileSystemEntries(Dir).Select(Path.GetFileName).Order());
    }

    /// <summary>Reports synchronously on the converter's thread, so the test can act in the middle of a job.</summary>
    sealed class OnFirstReport(Action action) : IProgress<double>
    {
        int _fired;
        public void Report(double value)
        {
            if (Interlocked.Exchange(ref _fired, 1) == 0) action();
        }
    }

    [Fact]
    public async Task FileAppearingAtTargetDuringJob_IsNeverOverwritten()
    {
        var png = Solid(Dir, "a.png", MagickColors.Red);
        var target = Path.Combine(Dir, "a.webp");
        var claimedByOther = false;
        var ctx = new OperationContext(CancellationToken.None, new OnFirstReport(() =>
        {
            // Another program creating the name without overwriting (as Explorer or a careful app would).
            try
            {
                using var other = new FileStream(target, FileMode.CreateNew);
                other.Write("someone else's"u8);
                claimedByOther = true;
            }
            catch (IOException) { /* the job claimed it first */ }
        }));

        var output = Assert.Single(await new ConversionRouter([new ImageConverter()]).ConvertAsync(png, "webp", ctx));

        Assert.Equal("red", CenterColor(output));
        if (claimedByOther)
        {
            Assert.NotEqual(target, output);
            Assert.Equal("someone else's", File.ReadAllText(target));
        }
        else Assert.Equal(target, output);
    }
}
