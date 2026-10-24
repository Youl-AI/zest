using Zest.Core.Jobs;

namespace Zest.Core.Convert;

public interface IConverter
{
    bool Handles(string from, string to);

    /// <summary>
    /// Returns the created result paths (a single folder for folder results).
    /// Contract: reserve every output where its name is chosen, with <c>OutputNaming.ConvertOutput/ToolOutput/CreatePagesFolder</c>
    /// (they claim the name atomically: an empty placeholder file created with <c>FileMode.CreateNew</c>, or a new folder, and
    /// then <c>ctx.Track</c> it), and write only over your own placeholder. On failure the router deletes exactly the tracked
    /// paths (tracked folders recursively), so never track a path you did not create yourself.
    /// </summary>
    Task<IReadOnlyList<string>> ConvertAsync(string source, string to, OperationContext ctx);
}
