using Zest.Core.Catalog;
using Zest.Core.Jobs;

namespace Zest.Core.Convert;

public sealed class ConversionRouter(IEnumerable<IConverter> converters)
{
    readonly IReadOnlyList<IConverter> _converters = converters.ToList();

    public async Task<IReadOnlyList<string>> ConvertAsync(string source, string to, OperationContext ctx)
    {
        var from = FormatCatalog.FormatOf(source);
        var converter = from is null ? null : _converters.FirstOrDefault(c => c.Handles(from, to));
        if (converter is null) throw new ZestException("이 변환은 지원하지 않습니다.");

        var dir = Path.GetDirectoryName(Path.GetFullPath(source))!;
        EnsureWritable(dir);
        // On any failure the tracked outputs are removed; IO and permission errors become a Korean ZestException.
        return await ToolRunner.RunAsync(ctx, () => converter.ConvertAsync(source, to, ctx));
    }

    static void EnsureWritable(string dir)
    {
        var probe = Path.Combine(dir, $".zest-write-test-{Guid.NewGuid():N}");
        try
        {
            using (File.Create(probe)) { }
            File.Delete(probe);
        }
        catch (Exception e) when (e is UnauthorizedAccessException or IOException)
        {
            throw new ZestException("이 폴더에는 저장할 수 없습니다.");
        }
    }
}
