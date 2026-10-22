using LimeWin.Core.Catalog;
using LimeWin.Core.Jobs;

namespace LimeWin.Core.Convert;

public sealed class ConversionRouter(IEnumerable<IConverter> converters)
{
    readonly IReadOnlyList<IConverter> _converters = converters.ToList();

    public async Task<IReadOnlyList<string>> ConvertAsync(string source, string to, OperationContext ctx)
    {
        var from = FormatCatalog.FormatOf(source);
        var converter = from is null ? null : _converters.FirstOrDefault(c => c.Handles(from, to));
        if (converter is null) throw new LimeException("이 변환은 지원하지 않습니다.");

        var dir = Path.GetDirectoryName(Path.GetFullPath(source))!;
        EnsureWritable(dir);
        try
        {
            return await converter.ConvertAsync(source, to, ctx);
        }
        catch
        {
            foreach (var path in ctx.Created)
            {
                try
                {
                    if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
                    else File.Delete(path);
                }
                catch (Exception) { /* best effort; the original failure matters more */ }
            }
            throw;
        }
    }

    static void EnsureWritable(string dir)
    {
        var probe = Path.Combine(dir, $".lime-write-test-{Guid.NewGuid():N}");
        try
        {
            using (File.Create(probe)) { }
            File.Delete(probe);
        }
        catch (Exception e) when (e is UnauthorizedAccessException or IOException)
        {
            throw new LimeException("이 폴더에는 저장할 수 없습니다.");
        }
    }
}
