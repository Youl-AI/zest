namespace Zest.Core.Tests;

internal static class TestFiles
{
    public static string TempDir()
    {
        var d = Path.Combine(Path.GetTempPath(), "zest-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(d);
        return d;
    }

    public static string Touch(string dir, string name)
    {
        var p = Path.Combine(dir, name);
        File.WriteAllText(p, "");
        return p;
    }
}
