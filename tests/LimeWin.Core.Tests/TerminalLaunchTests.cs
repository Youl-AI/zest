using LimeWin.Core.Actions;

namespace LimeWin.Core.Tests;

public class TerminalLaunchTests : IDisposable
{
    readonly string Dir = TestFiles.TempDir();

    public void Dispose() { try { Directory.Delete(Dir, true); } catch (IOException) { } }

    [Fact]
    public void FolderFor_Folder_IsItself()
    {
        var folder = Directory.CreateDirectory(Path.Combine(Dir, "폴더")).FullName;

        Assert.Equal(folder, TerminalLaunch.FolderFor(folder));
    }

    [Fact]
    public void FolderFor_File_IsItsParent()
    {
        var file = TestFiles.Touch(Dir, "a.txt");

        Assert.Equal(Dir, TerminalLaunch.FolderFor(file));
    }

    [Fact]
    public void WindowsTerminal_PassesFolderAsOneArgument()
    {
        Assert.Equal(["-d", @"C:\My Files\프로젝트"], TerminalLaunch.WindowsTerminalArgs(@"C:\My Files\프로젝트"));
    }

    [Fact]
    public void WindowsTerminal_NotUsedWhenFolderHasSemicolon()
    {
        // wt.exe splits its command line into subcommands at ';', so such a folder goes to PowerShell instead.
        Assert.False(TerminalLaunch.CanUseWindowsTerminal(@"C:\a;b"));
        Assert.True(TerminalLaunch.CanUseWindowsTerminal(@"C:\a b"));
    }

    [Fact]
    public void PowerShell_SingleQuotesAreDoubled()
    {
        Assert.Equal(
            ["-NoExit", "-Command", @"Set-Location -LiteralPath 'C:\it''s $HOME [x]'"],
            TerminalLaunch.PowerShellArgs(@"C:\it's $HOME [x]"));
    }

    [Fact]
    public void PowerShell_TypographicQuotesAreDoubledToo()
    {
        // PowerShell treats ‘ ’ ‚ ‛ as single quotes as well.
        Assert.Equal(@"Set-Location -LiteralPath 'C:\‘‘a’’ ‚‚b‛‛'", TerminalLaunch.PowerShellArgs(@"C:\‘a’ ‚b‛")[2]);
    }
}
