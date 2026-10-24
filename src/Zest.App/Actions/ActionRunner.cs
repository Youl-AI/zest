using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using Zest.App.Shelf;
using Zest.Core;
using Zest.Core.Actions;
using Zest.Core.Catalog;
using Zest.Core.Output;
using Microsoft.Win32;

namespace Zest.App.Actions;

/// <summary>
/// Runs a drop on an action petal: 선반, 복사, 이동, 경로 복사, 터미널. Copy and copy path finish at once and are silent
/// on success; a failure shows a failed card like a job. Call on the UI thread.
/// </summary>
public static class ActionRunner
{
    static readonly ShelfManager Shelves = new();
    const int ClipboardAttempts = 2;
    static readonly TimeSpan ClipboardRetryDelay = TimeSpan.FromMilliseconds(100);

    /// <param name="screenPx">Drop point in physical screen pixels (the shelf opens there, the folder dialog on that monitor).</param>
    public static void Run(DropAction action, IReadOnlyList<string> files, Point screenPx)
    {
        if (files.Count == 0) return;
        switch (action)
        {
            case DropAction.Shelf:
                Shelves.Add(files, screenPx);
                break;
            case DropAction.Copy:
                CopyFiles(files);
                break;
            case DropAction.Move:
                Move(files, screenPx);
                break;
            case DropAction.CopyPath:
                CopyPaths(files);
                break;
            case DropAction.Terminal:
                OpenTerminal(files[0]);
                break;
        }
    }

    /// <summary>Files on the clipboard as Explorer's Ctrl+C puts them, so Ctrl+V in Explorer pastes copies.</summary>
    static void CopyFiles(IReadOnlyList<string> files)
    {
        var list = new StringCollection();
        list.AddRange([.. files]);
        var data = new DataObject();
        data.SetFileDropList(list);
        // "Preferred DropEffect" tells the paste target copy (DROPEFFECT_COPY = 1) rather than cut.
        data.SetData("Preferred DropEffect", new MemoryStream(BitConverter.GetBytes((int)DragDropEffects.Copy)));
        SetClipboard(data, $"{Several(files)} 복사");
    }

    static void CopyPaths(IReadOnlyList<string> files)
    {
        var data = new DataObject();
        data.SetText(string.Join("\r\n", files));
        SetClipboard(data, $"{Several(files)} 경로 복사");
    }

    /// <summary>
    /// Another program may hold the clipboard open for a moment (CLIPBRD_E_CANT_OPEN). WPF already retries for about a
    /// second inside each call; one more attempt after a short pause, then a failed card.
    /// </summary>
    static void SetClipboard(DataObject data, string title)
    {
        if (!TrySetClipboard(data)) FailedCard(title, ClipboardBusyMessage);
    }

    internal const string ClipboardBusyMessage = "클립보드를 쓸 수 없습니다. 다른 프로그램이 사용 중입니다.";

    /// <summary>Puts <paramref name="data"/> on the clipboard (with the retry above); false when it stayed busy. UI thread only.</summary>
    internal static bool TrySetClipboard(DataObject data)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                Clipboard.SetDataObject(data, copy: true);
                return true;
            }
            catch (ExternalException) when (attempt < ClipboardAttempts)
            {
                Thread.Sleep(ClipboardRetryDelay);
            }
            catch (ExternalException)
            {
                return false;
            }
        }
    }

    static void Move(IReadOnlyList<string> files, Point at)
    {
        var dialog = new OpenFolderDialog
        {
            Title = files.Count == 1 ? $"‘{Name(files[0])}’을 옮길 폴더" : $"항목 {files.Count}개를 옮길 폴더",
            InitialDirectory = Path.GetDirectoryName(Path.GetFullPath(files[0])) ?? "",
        };
        var owner = DialogOwner(at);
        bool? chosen;
        try
        {
            chosen = dialog.ShowDialog(owner);
        }
        finally
        {
            owner.Close();
        }
        if (chosen != true) return;

        var destination = dialog.FolderName;
        var dispatcher = Application.Current.Dispatcher;
        Services.Jobs.Run($"{files.Count}개 옮기는 중", ctx =>
        {
            try
            {
                var result = FileMover.Move(files, destination, ctx);
                if (result.Failures.Count > 0) throw new ZestException(FileMover.FailureSummary(result));
                return Task.FromResult(result.Moved);
            }
            finally
            {
                dispatcher.BeginInvoke(Shelves.RefreshAll); // moved files on a shelf show as gone
            }
        });
    }

    /// <summary>
    /// An invisible owner on the drop monitor, brought to the front first: a tray app is not the foreground process after
    /// a drop, and an ownerless dialog could open behind Explorer.
    /// </summary>
    static Window DialogOwner(Point at)
    {
        var owner = new Window
        {
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            Width = 1,
            Height = 1,
            MinWidth = 0, // override the WPF-UI implicit Window style's minimum size
            MinHeight = 0,
            WindowStartupLocation = WindowStartupLocation.Manual,
        };
        Native.CenterInWorkArea(owner, (int)at.X, (int)at.Y);
        owner.Show();
        Native.BringToFront(owner);
        return owner;
    }

    /// <summary>Windows Terminal in the folder (the first item, or the folder it is in); PowerShell when wt.exe is missing or cannot take the path.</summary>
    static void OpenTerminal(string first)
    {
        var folder = TerminalLaunch.FolderFor(first);
        if (TerminalLaunch.CanUseWindowsTerminal(folder) && WindowsTerminal() is { } wt && TryStart(wt, TerminalLaunch.WindowsTerminalArgs(folder), folder))
            return;
        var powershell = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
        if (TryStart(powershell, TerminalLaunch.PowerShellArgs(folder), folder)) return;
        FailedCard($"{Name(folder)} 터미널", "터미널을 열지 못했습니다.");
    }

    /// <summary>wt.exe's App Execution Alias, else wt.exe on PATH, else null.</summary>
    static string? WindowsTerminal()
    {
        var alias = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WindowsApps", "wt.exe");
        return File.Exists(alias) ? alias : PathLookup.Find("wt.exe", Environment.GetEnvironmentVariable("PATH"));
    }

    /// <summary>Starts a program with each argument passed as one argument (never pasted into a command string).</summary>
    static bool TryStart(string exe, IReadOnlyList<string> args, string folder)
    {
        var start = new ProcessStartInfo(exe) { UseShellExecute = false, WorkingDirectory = folder };
        foreach (var arg in args) start.ArgumentList.Add(arg);
        try
        {
            Process.Start(start)?.Dispose();
            return true;
        }
        catch (Win32Exception)
        {
            return false;
        }
    }

    static void FailedCard(string title, string message) =>
        Services.Jobs.Run(title, _ => Task.FromException<IReadOnlyList<string>>(new ZestException(message)));

    static string Name(string path) => OutputNaming.DisplayName(path);

    /// <summary>"보고서.pdf" or "보고서.pdf 외 2개".</summary>
    static string Several(IReadOnlyList<string> files) =>
        files.Count == 1 ? Name(files[0]) : $"{Name(files[0])} 외 {files.Count - 1}개";
}
