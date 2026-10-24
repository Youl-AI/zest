using Zest.Core.Catalog;
using Zest.Core.Jobs;

namespace Zest.Core.Metadata;

/// <summary>
/// Writes a copy of <c>source</c> with <see cref="MetadataEdit"/> applied, next to it as `name (edited).ext` or
/// `name (no metadata).ext`; the source is never modified. Dispatches by family: images now, PDFs (Task 3) and media (Task 4) next.
/// </summary>
public static class MetadataWriter
{
    const string NotEditable = "이 형식은 메타데이터를 바꿀 수 없습니다.";

    public static Task<string> ApplyAsync(string source, MetadataEdit edit, OperationContext ctx) =>
        ToolRunner.RunAsync(ctx, () =>
        {
            var format = FormatCatalog.FormatOf(source);
            return (format is null ? null : FormatCatalog.CategoryOf(format)) switch
            {
                FormatCategory.Image => ctx.InSlotAsync(EngineGates.Image, () => Task.Run(() => ImageMetadata.Write(source, edit, ctx), ctx.Token)),
                // FormatCategory.Pdf (EngineGates.Image): Task 3. FormatCategory.Audio or FormatCategory.Video (EngineGates.Ffmpeg): Task 4.
                _ => throw new ZestException(NotEditable),
            };
        });
}
