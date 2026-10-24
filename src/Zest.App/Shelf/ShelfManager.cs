using System.Windows;

namespace Zest.App.Shelf;

/// <summary>Owns the open shelves. Every 선반 drop opens a new one; several can be open at once. Call on the UI thread.</summary>
public sealed class ShelfManager
{
    readonly List<ShelfWindow> _shelves = [];

    /// <param name="screenPx">Drop point in physical screen pixels; the shelf opens centred there.</param>
    public void Add(IReadOnlyList<string> files, Point screenPx)
    {
        var shelf = new ShelfWindow(files);
        shelf.Closed += (_, _) => _shelves.Remove(shelf);
        _shelves.Add(shelf);
        shelf.ShowAt((int)screenPx.X, (int)screenPx.Y);
    }

    /// <summary>Dims rows whose files were moved or deleted (e.g. by a 이동 job).</summary>
    public void RefreshAll()
    {
        foreach (var shelf in _shelves) shelf.RefreshMissing();
    }
}
