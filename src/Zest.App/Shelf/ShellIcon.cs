using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Zest.App.Shelf;

/// <summary>Explorer's small or large icon for a file or folder (SHGetFileInfo). Call on an STA thread (the UI thread, or <see cref="LoadLater"/>'s own).</summary>
internal static class ShellIcon
{
    static readonly BlockingCollection<Action> Pending = new();

    /// <summary>One background STA thread for icon reads, so a slow or offline network path never blocks the UI thread.</summary>
    static readonly Lazy<Thread> Reader = new(() =>
    {
        var thread = new Thread(() =>
        {
            foreach (var read in Pending.GetConsumingEnumerable())
            {
                try { read(); }
                catch (Exception) { /* one unreadable item must not stop the others */ }
            }
        })
        { IsBackground = true, Name = "Zest shell icons" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return thread;
    });

    /// <summary>
    /// Reads the item's small and large icons (<see cref="For"/>) and whether it exists on the background reader, then calls
    /// <paramref name="done"/> on <paramref name="dispatcher"/>'s thread. The icons are frozen, so they cross threads safely.
    /// </summary>
    public static void LoadLater(string path, Dispatcher dispatcher, Action<ImageSource?, ImageSource?, bool> done)
    {
        _ = Reader.Value;
        Pending.Add(() =>
        {
            var small = For(path);
            var large = For(path, large: true);
            var exists = File.Exists(path) || Directory.Exists(path);
            dispatcher.BeginInvoke(() => done(small, large, exists));
        });
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string szTypeName;
    }

    const uint SHGFI_ICON = 0x100, SHGFI_LARGEICON = 0x0, SHGFI_SMALLICON = 0x1, SHGFI_USEFILEATTRIBUTES = 0x10;
    const uint FILE_ATTRIBUTE_DIRECTORY = 0x10, FILE_ATTRIBUTE_NORMAL = 0x80;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr SHGetFileInfo(string path, uint attributes, ref SHFILEINFO info, uint size, uint flags);

    [DllImport("user32.dll")]
    static extern bool DestroyIcon(IntPtr icon);

    /// <summary>
    /// The item's own icon (an exe's, a folder's), or the generic icon for its type when it cannot be read. Small = the list
    /// row's 16 px icon, large = the grid tile's 32 px one (both at the system DPI).
    /// </summary>
    public static ImageSource? For(string path, bool large = false) =>
        Get(path, 0, SHGFI_ICON | SizeFlag(large)) ?? ForName(path, Directory.Exists(path), large);

    /// <summary>The icon for a name's type without touching the disk (the item need not exist).</summary>
    public static ImageSource? ForName(string name, bool isFolder, bool large = false) =>
        Get(name, isFolder ? FILE_ATTRIBUTE_DIRECTORY : FILE_ATTRIBUTE_NORMAL, SHGFI_ICON | SizeFlag(large) | SHGFI_USEFILEATTRIBUTES);

    static uint SizeFlag(bool large) => large ? SHGFI_LARGEICON : SHGFI_SMALLICON;

    static ImageSource? Get(string path, uint attributes, uint flags)
    {
        var info = new SHFILEINFO();
        if (SHGetFileInfo(path, attributes, ref info, (uint)Marshal.SizeOf<SHFILEINFO>(), flags) == IntPtr.Zero || info.hIcon == IntPtr.Zero)
            return null;
        try
        {
            var bitmap = Imaging.CreateBitmapSourceFromHIcon(info.hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            bitmap.Freeze();
            return bitmap;
        }
        finally
        {
            DestroyIcon(info.hIcon);
        }
    }
}
