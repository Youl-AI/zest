using Zest.Core.Catalog;

namespace Zest.Core.Jobs;

/// <summary>Which wheel a job came from; its progress card takes that wheel's citrus.</summary>
public enum JobKind { Convert, Tool, Action }

public static class JobKinds
{
    /// <summary>The wheel a kind of job is started from (convert = Shift, tools = Alt, actions = Ctrl).</summary>
    public static WheelMode ModeOf(JobKind kind) => kind switch
    {
        JobKind.Convert => WheelMode.Convert,
        JobKind.Tool => WheelMode.Tools,
        JobKind.Action => WheelMode.Actions,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };
}
