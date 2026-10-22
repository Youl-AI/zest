using LimeWin.Core.Jobs;

namespace LimeWin.Core.Convert;

public interface IConverter
{
    bool Handles(string from, string to);

    /// <summary>Returns the created result paths (a single folder for folder results).</summary>
    Task<IReadOnlyList<string>> ConvertAsync(string source, string to, OperationContext ctx);
}
