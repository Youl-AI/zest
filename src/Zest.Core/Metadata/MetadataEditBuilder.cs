namespace Zest.Core.Metadata;

/// <summary>Turns the metadata window's state into a <see cref="MetadataEdit"/>.</summary>
public static class MetadataEditBuilder
{
    /// <param name="original">The tags as read.</param>
    /// <param name="current">The editable fields' text by tag id (other ids are ignored).</param>
    /// <param name="removed">Ids struck with ×.</param>
    /// <param name="removeLocation">The banner's "위치 지우기": location rows are left to the writer's switch.</param>
    /// <param name="removeAll">"태그 모두 지우기": everything else in the window no longer counts.</param>
    /// <remarks>
    /// A text counts as changed when its trimmed value differs from the trimmed original; an editable field emptied this
    /// way is a removal, not a change to "". Ids not in <paramref name="original"/>, or not removable, are never removed.
    /// </remarks>
    public static MetadataEdit Build(
        IReadOnlyList<MetadataTag> original,
        IReadOnlyDictionary<string, string> current,
        IReadOnlySet<string> removed,
        bool removeLocation,
        bool removeAll)
    {
        if (removeAll) return MetadataEdit.Everything;

        var changed = new Dictionary<string, string>(StringComparer.Ordinal);
        var gone = new HashSet<string>(StringComparer.Ordinal);
        foreach (var tag in original)
        {
            if (removeLocation && IsLocation(tag.Id)) continue;
            if (removed.Contains(tag.Id))
            {
                if (tag.Removable) gone.Add(tag.Id);
                continue;
            }
            if (!tag.Editable || !current.TryGetValue(tag.Id, out var text)) continue;
            var now = text.Trim();
            if (now == tag.Value.Trim()) continue;
            if (now.Length > 0) changed[tag.Id] = now;
            else if (tag.Removable) gone.Add(tag.Id);
        }
        return new MetadataEdit(changed, gone, removeLocation, RemoveAll: false);
    }

    /// <summary>Whether a tag id ("{family}:{key}") is a location tag, per <see cref="MetadataLabels.IsLocationKey"/>.</summary>
    public static bool IsLocation(string id)
    {
        var colon = id.IndexOf(':');
        return colon > 0 && MetadataLabels.IsLocationKey(id[..colon], id[(colon + 1)..]);
    }
}
