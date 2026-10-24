using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Zest.Core.Jobs;

namespace Zest.Core.Tests;

/// <summary>A shared attempt nobody awaits any more (its caller cancelled) must not surface as UnobservedTaskException (final review M6).</summary>
public class TaskObservationTests
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    static void FailUnawaited(string message, bool observe)
    {
        var task = Task.Run(() => throw new InvalidOperationException(message));
        SpinWait.SpinUntil(() => task.IsCompleted); // completes without anyone reading its exception
        if (observe) task.ObserveFaults().Wait(TestContext.Current.CancellationToken);
    }

    [Fact]
    public void ObserveFaults_KeepsAFailureOutOfUnobservedTaskException()
    {
        var marker = Guid.NewGuid().ToString("N");
        var seen = new ConcurrentBag<string>();
        void Handler(object? sender, UnobservedTaskExceptionEventArgs e)
        {
            foreach (var x in e.Exception.InnerExceptions) seen.Add(x.Message);
        }
        TaskScheduler.UnobservedTaskException += Handler;
        try
        {
            FailUnawaited("observed " + marker, observe: true);
            FailUnawaited("unobserved " + marker, observe: false);
            for (var i = 0; i < 5 && !seen.Contains("unobserved " + marker); i++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }
            Assert.Contains("unobserved " + marker, seen); // the probe sees an unobserved failure
            Assert.DoesNotContain("observed " + marker, seen);
        }
        finally
        {
            TaskScheduler.UnobservedTaskException -= Handler;
        }
    }
}
