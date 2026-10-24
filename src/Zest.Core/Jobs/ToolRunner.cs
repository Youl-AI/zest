namespace Zest.Core.Jobs;

/// <summary>Runs a tool operation and, like <c>ConversionRouter</c>, removes the tracked outputs if it fails or is cancelled. Write failures (IO, access denied) become a Korean <see cref="ZestException"/>.</summary>
public static class ToolRunner
{
    public static async Task<T> RunAsync<T>(OperationContext ctx, Func<Task<T>> work)
    {
        try
        {
            return await work();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            TrackedCleanup.DeleteCreated(ctx);
            throw new ZestException("파일을 읽거나 저장하지 못했습니다.");
        }
        catch
        {
            TrackedCleanup.DeleteCreated(ctx);
            throw;
        }
    }
}
