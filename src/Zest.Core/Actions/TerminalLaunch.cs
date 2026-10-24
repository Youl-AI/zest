namespace Zest.Core.Actions;

/// <summary>
/// Arguments for the 터미널 action. Each path travels as one argument (the caller uses <c>ProcessStartInfo.ArgumentList</c>),
/// and the one place a path lands inside a command string (PowerShell's <c>-Command</c>) is a single-quoted literal with its quotes doubled.
/// </summary>
public static class TerminalLaunch
{
    /// <summary>The folder itself, or a file's parent folder.</summary>
    public static string FolderFor(string path) =>
        Directory.Exists(path) ? path : Path.GetDirectoryName(Path.GetFullPath(path)) ?? path;

    /// <summary>wt.exe splits its command line into subcommands at ';', so a folder with one goes to PowerShell instead.</summary>
    public static bool CanUseWindowsTerminal(string folder) => !folder.Contains(';');

    /// <summary><c>wt.exe -d &lt;folder&gt;</c>.</summary>
    public static IReadOnlyList<string> WindowsTerminalArgs(string folder) => ["-d", folder];

    /// <summary><c>powershell.exe -NoExit -Command "Set-Location -LiteralPath '&lt;folder&gt;'"</c>.</summary>
    public static IReadOnlyList<string> PowerShellArgs(string folder) =>
        ["-NoExit", "-Command", $"Set-Location -LiteralPath '{SingleQuoted(folder)}'"];

    /// <summary>PowerShell single-quoted string content: every quote PowerShell accepts as a single quote (' ‘ ’ ‚ ‛) is doubled.</summary>
    static string SingleQuoted(string text)
    {
        var sb = new System.Text.StringBuilder(text.Length + 4);
        foreach (var c in text)
        {
            sb.Append(c);
            if (c is '\'' or '‘' or '’' or '‚' or '‛') sb.Append(c);
        }
        return sb.ToString();
    }
}
