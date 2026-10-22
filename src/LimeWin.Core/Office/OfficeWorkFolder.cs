namespace LimeWin.Core.Office;

/// <summary>
/// The private <c>%TEMP%\LimeWin\&lt;guid&gt;</c> folder one Office conversion reads and writes in (<c>in\</c>, <c>out\</c>),
/// deleted on Dispose. Office only ever opens a private copy of the source: opening the user's path would hand back their
/// own document if it is already open in that instance, and SaveAs + Close would then take it from them.
/// </summary>
sealed class OfficeWorkFolder : IDisposable
{
    readonly string _root;

    OfficeWorkFolder(string root)
    {
        _root = root;
        In = Directory.CreateDirectory(Path.Combine(root, "in")).FullName;
        Out = Directory.CreateDirectory(Path.Combine(root, "out")).FullName;
    }

    public string In { get; }
    public string Out { get; }

    public static OfficeWorkFolder Create() =>
        new(Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "LimeWin", Guid.NewGuid().ToString("N"))).FullName);

    /// <summary>A fresh <c>in\&lt;guid&gt;&lt;extension&gt;</c> path (extension with its dot).</summary>
    public string NewInputPath(string extension) => Path.Combine(In, $"{Guid.NewGuid():N}{extension}");

    /// <summary>Copies the source to a fresh input path with <paramref name="extension"/> and returns it.</summary>
    public string CopySource(string source, string extension)
    {
        var input = NewInputPath(extension);
        try { File.Copy(source, input); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw new LimeException("원본 파일을 열 수 없습니다.");
        }
        return input;
    }

    /// <summary>Office opens a 0-byte file as a blank document; converting nothing is not a result.</summary>
    public static void EnsureNotEmpty(string source)
    {
        long length;
        try { length = new FileInfo(source).Length; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw new LimeException("원본 파일을 열 수 없습니다.");
        }
        if (length == 0) throw new LimeException("빈 파일은 변환할 수 없습니다.");
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* temp leftovers are harmless */ }
    }
}
