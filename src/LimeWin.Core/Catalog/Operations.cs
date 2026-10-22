namespace LimeWin.Core.Catalog;

public enum WheelMode { Convert, Tools, Actions }

public enum LimeTool { Compress, Crop, ImagesToPdf, MergePdfs, SplitPdf, CopyText, RemoveBackground }

public enum DropAction { Shelf, Copy, Move, CopyPath, Terminal }

public abstract record Operation;

public sealed record ConvertOp(string Target) : Operation;

public sealed record ToolOp(LimeTool Tool) : Operation;

public sealed record ActionOp(DropAction Action) : Operation;

public sealed record WheelItem(Operation Operation, string Label);
