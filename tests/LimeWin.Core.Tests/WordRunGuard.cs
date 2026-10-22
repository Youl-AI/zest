using LimeWin.Core.Office;

[assembly: AssemblyFixture(typeof(LimeWin.Core.Tests.WordRunGuard))]

namespace LimeWin.Core.Tests;

/// <summary>
/// Test-run guard: fails the run when a WINWORD.EXE that started during the run is still alive at the end.
/// It only reports; it never kills (a Word the user opened mid-run must not be touched).
/// </summary>
public sealed class WordRunGuard : IDisposable
{
    readonly IReadOnlySet<OfficeProcess> _before = OfficeProcesses.Snapshot("WINWORD");

    public void Dispose()
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(15);
        IReadOnlyList<OfficeProcess> left;
        while ((left = Leftovers()).Count > 0 && DateTime.UtcNow < deadline) Thread.Sleep(250);
        if (left.Count > 0)
            throw new InvalidOperationException(
                $"WINWORD.EXE started during the test run is still running: PID {string.Join(", ", left.Select(p => p.Id))}");
    }

    IReadOnlyList<OfficeProcess> Leftovers() => OfficeProcesses.Snapshot("WINWORD").Except(_before).ToList();
}
