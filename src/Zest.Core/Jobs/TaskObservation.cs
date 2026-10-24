namespace Zest.Core.Jobs;

/// <summary>Helpers for tasks that may end with nobody awaiting them.</summary>
public static class TaskObservation
{
    /// <summary>
    /// Marks a failure of <paramref name="task"/> as observed, for a task whose awaiters may all have gone (cancelled), so it
    /// never reaches <see cref="TaskScheduler.UnobservedTaskException"/>. Returns the continuation (for tests).
    /// </summary>
    public static Task ObserveFaults(this Task task) =>
        task.ContinueWith(t => _ = t.Exception, CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
}
