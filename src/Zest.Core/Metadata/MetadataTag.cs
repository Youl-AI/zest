namespace Zest.Core.Metadata;

/// <summary>One metadata entry shown in the editor. <paramref name="Id"/> is "{family}:{key}", family file|exif|xmp|iptc|pdf|media.</summary>
public sealed record MetadataTag(string Id, string Group, string Label, string Value, bool Editable, bool Removable);
