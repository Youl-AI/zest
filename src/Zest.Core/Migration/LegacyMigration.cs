namespace Zest.Core.Migration;

/// <summary>Brings an existing Lime install's data folder over to Zest.</summary>
public static class LegacyMigration
{
    public const string LegacyFolderName = "LimeWin";
    public const string FolderName = "Zest";

    /// <summary>
    /// Moves <c>&lt;localAppData&gt;\LimeWin</c> to <c>&lt;localAppData&gt;\Zest</c> and returns the number of
    /// top-level entries moved. Never overwrites anything in the target and never throws: an entry that
    /// cannot be moved stays in the legacy folder.
    /// </summary>
    public static int MoveDataFolder(string localAppData)
    {
        try
        {
            var legacy = Path.Combine(localAppData, LegacyFolderName);
            var target = Path.Combine(localAppData, FolderName);
            if (!Directory.Exists(legacy)) return 0;

            if (!Directory.Exists(target))
            {
                var count = Directory.EnumerateFileSystemEntries(legacy).Count();
                try
                {
                    Directory.Move(legacy, target);
                    return count;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    Directory.CreateDirectory(target); // fall back to moving entry by entry
                }
            }

            var moved = 0;
            foreach (var entry in Directory.EnumerateFileSystemEntries(legacy).ToList())
            {
                var dest = Path.Combine(target, Path.GetFileName(entry));
                if (File.Exists(dest) || Directory.Exists(dest)) continue;
                try
                {
                    if (Directory.Exists(entry)) Directory.Move(entry, dest);
                    else File.Move(entry, dest);
                    moved++;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }

            if (!Directory.EnumerateFileSystemEntries(legacy).Any()) Directory.Delete(legacy);
            return moved;
        }
        catch (Exception)
        {
            // Contract: startup migration is a convenience and must never stop the app.
            return 0;
        }
    }
}
