namespace Zest.Core;

/// <summary>A user-presentable conversion/tool failure.</summary>
public sealed class ZestException(string message) : Exception(message);
