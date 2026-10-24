using Zest.Core.Jobs;

namespace Zest.Core.Tests;

public class ToolRunnerTests : IDisposable
{
    readonly string Dir = TestFiles.TempDir();

    public void Dispose() { try { Directory.Delete(Dir, true); } catch (IOException) { } }

    [Theory]
    [InlineData(typeof(IOException))]
    [InlineData(typeof(UnauthorizedAccessException))]
    [InlineData(typeof(DirectoryNotFoundException))]
    public async Task IoFailure_BecomesLimeException_AfterCleanup(Type exceptionType)
    {
        var ctx = new OperationContext(CancellationToken.None);
        var path = TestFiles.Touch(Dir, "결과.txt");
        var ex = await Assert.ThrowsAsync<ZestException>(() => ToolRunner.RunAsync<string>(ctx, () =>
        {
            ctx.Track(path);
            throw (Exception)Activator.CreateInstance(exceptionType)!;
        }));
        Assert.Equal("파일을 읽거나 저장하지 못했습니다.", ex.Message);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task ZestException_PassesThroughUnchanged_AfterCleanup()
    {
        var ctx = new OperationContext(CancellationToken.None);
        var path = TestFiles.Touch(Dir, "결과.txt");
        var ex = await Assert.ThrowsAsync<ZestException>(() => ToolRunner.RunAsync<string>(ctx, () =>
        {
            ctx.Track(path);
            throw new ZestException("원래 메시지");
        }));
        Assert.Equal("원래 메시지", ex.Message);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task Cancellation_PassesThroughUnchanged_AfterCleanup()
    {
        var ctx = new OperationContext(CancellationToken.None);
        var path = TestFiles.Touch(Dir, "결과.txt");
        await Assert.ThrowsAsync<OperationCanceledException>(() => ToolRunner.RunAsync<string>(ctx, () =>
        {
            ctx.Track(path);
            throw new OperationCanceledException();
        }));
        Assert.False(File.Exists(path));
    }
}
