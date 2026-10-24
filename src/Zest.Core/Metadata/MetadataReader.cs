using Zest.Core.Catalog;

namespace Zest.Core.Metadata;

/// <summary>Lists the tags of one file for the editor, by family: images now, PDFs (Task 3) and media (Task 4) next.</summary>
public static class MetadataReader
{
    const string NotEditable = "이 형식은 메타데이터를 바꿀 수 없습니다.";

    public static async Task<IReadOnlyList<MetadataTag>> ReadAsync(string path, Capabilities caps, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var format = FormatCatalog.FormatOf(path);
        if (format is null || !MetadataFormats.Supports(format, caps)) throw new ZestException(NotEditable);
        return FormatCatalog.CategoryOf(format) switch
        {
            FormatCategory.Image => await Task.Run(() => ImageMetadata.Read(path, format, token), token),
            // FormatCategory.Pdf: Task 3. FormatCategory.Audio or FormatCategory.Video: Task 4.
            _ => throw new ZestException(NotEditable),
        };
    }
}
