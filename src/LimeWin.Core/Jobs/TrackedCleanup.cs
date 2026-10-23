namespace LimeWin.Core.Jobs;

/// <summary>Removes what a failed or cancelled operation created. Only paths registered with <see cref="OperationContext.Track"/> (which only ever holds paths the operation itself reserved or created) are touched.</summary>
public static class TrackedCleanup
{
    public static void DeleteCreated(OperationContext ctx)
    {
        foreach (var path in ctx.Created)
        {
            try
            {
                if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
                else File.Delete(path);
            }
            catch (Exception) { /* best effort; the original failure matters more */ }
        }
    }
}
