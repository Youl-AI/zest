using System.Diagnostics;
using LimeWin.Core.Office;

namespace LimeWin.Core.Tests;

/// <summary>The job ties a launched Office process to our lifetime; a stand-in child process plays Office here.</summary>
public class KillOnExitJobTests
{
    static (Process Proc, OfficeProcess Id) StartStandIn()
    {
        var p = Process.Start(new ProcessStartInfo("ping.exe", "-n 60 127.0.0.1") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true })!;
        return (p, new OfficeProcess(p.Id, p.StartTime));
    }

    [Fact]
    public void Closing_KillsTheProcess()
    {
        var (proc, id) = StartStandIn();
        using (proc)
        {
            var job = KillOnExitJob.TryCreate(id);
            Assert.NotNull(job);
            job.Dispose(); // what Windows does to the handle when our process dies, even when it is killed
            Assert.True(proc.WaitForExit(TimeSpan.FromSeconds(5)));
        }
    }

    [Fact]
    public void ClosingAJobForAProcessWeStarted_KillsIt()
    {
        // ffmpeg/ffprobe: started by us, so the Process object's own handle is used (no PID lookup).
        using var proc = StartStandIn().Proc;
        var job = KillOnExitJob.TryCreate(proc);
        Assert.NotNull(job);
        job.Dispose(); // what Windows does when Lime dies
        Assert.True(proc.WaitForExit(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void AnExitedProcess_GetsNoJob()
    {
        using var proc = Process.Start(new ProcessStartInfo("cmd.exe", "/c exit 0") { UseShellExecute = false, CreateNoWindow = true })!;
        proc.WaitForExit();
        using var job = KillOnExitJob.TryCreate(proc); // must not throw
    }

    [Fact]
    public void Released_LeavesTheProcessRunning()
    {
        var (proc, id) = StartStandIn();
        using (proc)
        {
            try
            {
                var job = KillOnExitJob.TryCreate(id)!;
                job.Release();
                job.Dispose();
                Assert.False(proc.WaitForExit(TimeSpan.FromSeconds(1)));
            }
            finally { proc.Kill(); }
        }
    }
}
