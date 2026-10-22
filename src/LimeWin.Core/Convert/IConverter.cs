using LimeWin.Core.Jobs;

namespace LimeWin.Core.Convert;

public interface IConverter
{
    bool Handles(string from, string to);

    /// <summary>
    /// Returns the created result paths (a single folder for folder results).
    /// Contract: call <c>ctx.Track(path)</c> for every output file or folder BEFORE creating it; on failure the
    /// router deletes exactly the tracked paths (tracked folders recursively), so only track what you create.
    /// </summary>
    Task<IReadOnlyList<string>> ConvertAsync(string source, string to, OperationContext ctx);
}
