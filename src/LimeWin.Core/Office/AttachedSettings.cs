using System.Runtime.InteropServices;

namespace LimeWin.Core.Office;

/// <summary>
/// The user's <c>DisplayAlerts</c> and <c>AutomationSecurity</c> on an instance we attached to, shared by every worker of
/// one host. The first item to enter captures them (before switching them off); they are put back when the last item in
/// flight leaves. Kept per host, not per worker: a worker that replaced an abandoned one still running on the same
/// instance would otherwise capture our off-values as the user's and restore those last.
/// </summary>
sealed class AttachedSettings
{
    readonly object _gate = new();
    int _inFlight;
    object? _alerts;
    object? _security;

    /// <summary>STA thread of the caller. Counts the item in only once the user's values are known.</summary>
    public void Enter(dynamic app)
    {
        lock (_gate)
        {
            if (_inFlight == 0)
            {
                _alerts = app.DisplayAlerts;
                _security = app.AutomationSecurity;
            }
            _inFlight++;
        }
    }

    /// <summary>STA thread of the caller. The last item out restores the user's values.</summary>
    public void Exit(dynamic app)
    {
        lock (_gate)
        {
            if (--_inFlight > 0) return;
            try
            {
                app.DisplayAlerts = _alerts;
                app.AutomationSecurity = _security;
            }
            catch (COMException) { } // the instance is gone or unreachable
        }
    }
}
