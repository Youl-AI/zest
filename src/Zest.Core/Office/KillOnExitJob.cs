using System.Runtime.InteropServices;

namespace Zest.Core.Office;

/// <summary>
/// A Win32 job object holding one Office process we launched, with kill-on-close: if Zest (or a test run) dies without
/// cleaning up, even by being killed, Windows closes the handle and the hidden Office goes with it.
/// <see cref="Release"/> clears kill-on-close first, for an instance we leave running on purpose (documents still open).
/// </summary>
sealed class KillOnExitJob : IDisposable
{
    const int JobObjectExtendedLimitInformation = 9;
    const uint LimitKillOnJobClose = 0x2000;
    const uint ProcessSetQuota = 0x0100, ProcessTerminate = 0x0001;

    readonly IntPtr _job;

    KillOnExitJob(IntPtr job) => _job = job;

    /// <summary>Puts the process in a new kill-on-close job; null when Windows refuses (then nothing changes).</summary>
    public static KillOnExitJob? TryCreate(OfficeProcess process)
    {
        var job = CreateJobObject(IntPtr.Zero, null);
        if (job == IntPtr.Zero) return null;
        var proc = IntPtr.Zero;
        try
        {
            if (!SetKillOnClose(job, true)) return Fail(job);
            if (!OfficeProcesses.IsAlive(process)) return Fail(job); // PID no longer the instance we saw
            proc = OpenProcess(ProcessSetQuota | ProcessTerminate, false, (uint)process.Id);
            if (proc == IntPtr.Zero || !AssignProcessToJobObject(job, proc)) return Fail(job);
            return new KillOnExitJob(job);
        }
        finally
        {
            if (proc != IntPtr.Zero) CloseHandle(proc);
        }
    }

    /// <summary>
    /// Puts a process we started ourselves (ffmpeg, ffprobe) in a new kill-on-close job, through its own handle; call right
    /// after <see cref="System.Diagnostics.Process.Start()"/> and dispose once it has exited. Null when Windows refuses or it already exited.
    /// </summary>
    public static KillOnExitJob? TryCreate(System.Diagnostics.Process process)
    {
        var job = CreateJobObject(IntPtr.Zero, null);
        if (job == IntPtr.Zero) return null;
        try
        {
            if (!SetKillOnClose(job, true) || !AssignProcessToJobObject(job, process.Handle)) return Fail(job);
            return new KillOnExitJob(job);
        }
        catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return Fail(job); // exited or no handle
        }
    }

    /// <summary>Lets the process outlive the job.</summary>
    public void Release() => SetKillOnClose(_job, false);

    public void Dispose() => CloseHandle(_job);

    static KillOnExitJob? Fail(IntPtr job)
    {
        CloseHandle(job);
        return null;
    }

    static bool SetKillOnClose(IntPtr job, bool on)
    {
        var info = new ExtendedLimitInformation { BasicLimitInformation = { LimitFlags = on ? LimitKillOnJobClose : 0 } };
        return SetInformationJobObject(job, JobObjectExtendedLimitInformation, ref info, (uint)Marshal.SizeOf<ExtendedLimitInformation>());
    }

    [StructLayout(LayoutKind.Sequential)]
    struct BasicLimitInformation
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize;
        public UIntPtr MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct IoCounters
    {
        public ulong ReadOperationCount, WriteOperationCount, OtherOperationCount, ReadTransferCount, WriteTransferCount, OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct ExtendedLimitInformation
    {
        public BasicLimitInformation BasicLimitInformation;
        public IoCounters IoInfo;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryUsed;
        public UIntPtr PeakJobMemoryUsed;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern IntPtr CreateJobObject(IntPtr attributes, string? name);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool SetInformationJobObject(IntPtr job, int infoClass, ref ExtendedLimitInformation info, uint length);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool CloseHandle(IntPtr handle);
}
