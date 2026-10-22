namespace LimeWin.Core;

/// <summary>A user-presentable conversion/tool failure.</summary>
public sealed class LimeException(string message) : Exception(message);
