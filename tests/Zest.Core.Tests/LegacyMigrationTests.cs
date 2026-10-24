using Zest.Core.Migration;

namespace Zest.Core.Tests;

public class LegacyMigrationTests : IDisposable
{
    readonly string Root = TestFiles.TempDir();

    public void Dispose() { try { Directory.Delete(Root, true); } catch (IOException) { } }

    string Legacy => Path.Combine(Root, LegacyMigration.LegacyFolderName);
    string Target => Path.Combine(Root, LegacyMigration.FolderName);

    [Fact]
    public void MoveDataFolder_NoLegacyFolder_ReturnsZero()
    {
        Assert.Equal(0, LegacyMigration.MoveDataFolder(Root));
        Assert.False(Directory.Exists(Target));
        Assert.False(Directory.Exists(Legacy));
    }

    [Fact]
    public void MoveDataFolder_OnlyLegacy_MovesWholeFolder()
    {
        var bytes = new byte[] { 1, 2, 3, 4 };
        Directory.CreateDirectory(Path.Combine(Legacy, "models"));
        File.WriteAllBytes(Path.Combine(Legacy, "models", "BiRefNet-matting-epoch_100.onnx"), bytes);

        Assert.Equal(1, LegacyMigration.MoveDataFolder(Root));

        Assert.Equal(bytes, File.ReadAllBytes(Path.Combine(Target, "models", "BiRefNet-matting-epoch_100.onnx")));
        Assert.False(Directory.Exists(Legacy));
    }

    [Fact]
    public void MoveDataFolder_BothExist_MovesOnlyMissingEntries()
    {
        Directory.CreateDirectory(Path.Combine(Legacy, "models"));
        File.WriteAllText(Path.Combine(Legacy, "models", "a.onnx"), "a");
        File.WriteAllText(Path.Combine(Legacy, "notes.txt"), "old");
        Directory.CreateDirectory(Target);
        File.WriteAllText(Path.Combine(Target, "notes.txt"), "new");

        Assert.Equal(1, LegacyMigration.MoveDataFolder(Root));

        Assert.True(File.Exists(Path.Combine(Target, "models", "a.onnx")));
        Assert.Equal("new", File.ReadAllText(Path.Combine(Target, "notes.txt")));
        Assert.Equal("old", File.ReadAllText(Path.Combine(Legacy, "notes.txt")));
    }

    [Fact]
    public void MoveDataFolder_LockedFile_LeavesItAndKeepsGoing()
    {
        Directory.CreateDirectory(Path.Combine(Legacy, "models"));
        var locked = Path.Combine(Legacy, "models", "locked.onnx");
        File.WriteAllText(locked, "x");
        File.WriteAllText(Path.Combine(Legacy, "other.txt"), "o");
        Directory.CreateDirectory(Target);

        int moved;
        using (new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.None))
            moved = LegacyMigration.MoveDataFolder(Root);

        Assert.True(File.Exists(Path.Combine(Target, "other.txt")));
        Assert.True(File.Exists(locked));
        Assert.Equal(1, moved);
    }

    [Fact]
    public void MoveDataFolder_OnlyLegacy_LockedFile_FallsBackPerEntry()
    {
        Directory.CreateDirectory(Path.Combine(Legacy, "models"));
        var locked = Path.Combine(Legacy, "models", "locked.onnx");
        File.WriteAllText(locked, "x");
        File.WriteAllText(Path.Combine(Legacy, "other.txt"), "o");

        int moved;
        using (new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.None))
            moved = LegacyMigration.MoveDataFolder(Root);

        Assert.True(File.Exists(Path.Combine(Target, "other.txt")));
        Assert.True(File.Exists(locked));
        Assert.Equal(1, moved);
    }
}
