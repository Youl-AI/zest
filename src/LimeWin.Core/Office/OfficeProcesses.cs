using System.ComponentModel;
using System.Diagnostics;

namespace LimeWin.Core.Office;

/// <summary>One process instance; the start time keeps a recycled PID from being mistaken for it.</summary>
public readonly record struct OfficeProcess(int Id, DateTime StartTime);

/// <summary>Process snapshots and PID-safe kill for Office apps.</summary>
public static class OfficeProcesses
{
    /// <summary>Running processes named <paramref name="processName"/> (no ".exe"), e.g. "WINWORD".</summary>
    public static IReadOnlySet<OfficeProcess> Snapshot(string processName)
    {
        var set = new HashSet<OfficeProcess>();
        foreach (var p in Process.GetProcessesByName(processName))
        {
            using (p)
            {
                try { set.Add(new(p.Id, p.StartTime)); }
                catch (Exception e) when (e is Win32Exception or InvalidOperationException) { /* exited or not ours to read */ }
            }
        }
        return set;
    }

    /// <summary>The one process that appeared between the snapshots; null when none or several did (then it is not provably ours).</summary>
    public static OfficeProcess? Launched(IReadOnlySet<OfficeProcess> before, IReadOnlySet<OfficeProcess> after)
    {
        var added = Added(before, after);
        return added.Count == 1 ? added[0] : null;
    }

    /// <summary>Processes in <paramref name="after"/> that were not in <paramref name="before"/>.</summary>
    public static IReadOnlyList<OfficeProcess> Added(IReadOnlySet<OfficeProcess> before, IReadOnlySet<OfficeProcess> after) =>
        after.Except(before).ToList();

    public static bool IsAlive(OfficeProcess p)
    {
        using var proc = Open(p);
        return proc is not null;
    }

    /// <summary>True when the process has exited (or was never found) within <paramref name="timeout"/>.</summary>
    public static bool WaitForExit(OfficeProcess p, TimeSpan timeout)
    {
        using var proc = Open(p);
        return proc is null || proc.WaitForExit(timeout);
    }

    /// <summary>Kills exactly this process instance, if it is still the same one, and waits briefly for it to go.</summary>
    public static void Kill(OfficeProcess p)
    {
        using var proc = Open(p);
        if (proc is null) return;
        try
        {
            proc.Kill();
            proc.WaitForExit(TimeSpan.FromSeconds(5));
        }
        catch (Exception e) when (e is Win32Exception or InvalidOperationException or NotSupportedException) { }
    }

    static Process? Open(OfficeProcess p)
    {
        Process? proc = null;
        try
        {
            proc = Process.GetProcessById(p.Id);
            if (!proc.HasExited && proc.StartTime == p.StartTime) return proc;
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or Win32Exception) { }
        proc?.Dispose();
        return null;
    }
}
