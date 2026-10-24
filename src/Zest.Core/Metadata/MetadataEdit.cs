namespace Zest.Core.Metadata;

/// <summary>What to change in a copy: new text values by tag id, tag ids to drop, and the two bulk switches.</summary>
public sealed record MetadataEdit(
    IReadOnlyDictionary<string, string> Changed,
    IReadOnlySet<string> Removed,
    bool RemoveLocation,
    bool RemoveAll)
{
    public static MetadataEdit LocationOnly { get; } =
        new(new Dictionary<string, string>(), new HashSet<string>(), RemoveLocation: true, RemoveAll: false);

    public static MetadataEdit Everything { get; } =
        new(new Dictionary<string, string>(), new HashSet<string>(), RemoveLocation: false, RemoveAll: true);
}
