using System.Runtime.InteropServices;

namespace Zest.Core.Office;

/// <summary>
/// COM message filter for the Office STA thread: when Office answers "busy" (SERVERCALL_RETRYLATER, which would surface
/// as RPC_E_CALL_REJECTED 0x80010001 / RPC_E_SERVERCALL_RETRYLATER 0x8001010A) the call is retried every 100 ms
/// until <c>retryLimitMs()</c> has passed since the call began.
/// </summary>
sealed class OfficeMessageFilter(Func<int> retryLimitMs) : IOleMessageFilter
{
    const int ServerCallIsHandled = 0;
    const int ServerCallRetryLater = 2;
    const int PendingMsgWaitDefProcess = 2;
    const int RetryAfterMs = 100;
    const int CancelCall = -1;

    public int HandleInComingCall(int dwCallType, IntPtr hTaskCaller, int dwTickCount, IntPtr lpInterfaceInfo) => ServerCallIsHandled;

    public int RetryRejectedCall(IntPtr hTaskCallee, int dwTickCount, int dwRejectType) =>
        dwRejectType == ServerCallRetryLater && dwTickCount < retryLimitMs() ? RetryAfterMs : CancelCall;

    public int MessagePending(IntPtr hTaskCallee, int dwTickCount, int dwPendingType) => PendingMsgWaitDefProcess;

    /// <summary>Installs <paramref name="filter"/> for the calling STA thread (null removes it).</summary>
    public static void Register(OfficeMessageFilter? filter) => CoRegisterMessageFilter(filter, out _);

    [DllImport("ole32.dll")]
    static extern int CoRegisterMessageFilter(IOleMessageFilter? newFilter, out IOleMessageFilter? oldFilter);
}

[ComImport, Guid("00000016-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IOleMessageFilter
{
    [PreserveSig] int HandleInComingCall(int dwCallType, IntPtr hTaskCaller, int dwTickCount, IntPtr lpInterfaceInfo);
    [PreserveSig] int RetryRejectedCall(IntPtr hTaskCallee, int dwTickCount, int dwRejectType);
    [PreserveSig] int MessagePending(IntPtr hTaskCallee, int dwTickCount, int dwPendingType);
}
