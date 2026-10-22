using LimeWin.Core.Office;

[assembly: AssemblyFixture(typeof(LimeWin.Core.Tests.OfficeRunGuard))]

namespace LimeWin.Core.Tests;

/// <summary>
/// Test-run guard: fails the run when a WINWORD.EXE, POWERPNT.EXE or EXCEL.EXE that started during the run is still
/// alive at the end. It only reports; it never kills (an Office app the user opened mid-run must not be touched).
/// </summary>
public sealed class OfficeRunGuard : IDisposable
{
    static readonly string[] Names = ["WINWORD", "POWERPNT", "EXCEL"];
    readonly Dictionary<string, IReadOnlySet<OfficeProcess>> _before = Names.ToDictionary(n => n, OfficeProcesses.Snapshot);

    public void Dispose()
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(15);
        IReadOnlyList<string> left;
        while ((left = Leftovers()).Count > 0 && DateTime.UtcNow < deadline) Thread.Sleep(250);
        if (left.Count > 0)
            throw new InvalidOperationException($"Office started during the test run is still running: {string.Join(", ", left)}");
    }

    IReadOnlyList<string> Leftovers() =>
        Names.SelectMany(n => OfficeProcesses.Snapshot(n).Except(_before[n]).Select(p => $"{n}.EXE PID {p.Id}")).ToList();
}
