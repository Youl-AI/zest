namespace LimeWin.Core.Jobs;

/// <summary>Runs a tool operation and, like <c>ConversionRouter</c>, removes the tracked outputs if it fails or is cancelled.</summary>
public static class ToolRunner
{
    public static async Task<T> RunAsync<T>(OperationContext ctx, Func<Task<T>> work)
    {
        try
        {
            return await work();
        }
        catch
        {
            TrackedCleanup.DeleteCreated(ctx);
            throw;
        }
    }
}
