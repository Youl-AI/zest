using Zest.Core.Catalog;
using Zest.Core.Jobs;

namespace Zest.Core.Tests;

public class JobKindTests
{
    [Fact]
    public void ModeOf_MapsEachKind()
    {
        Assert.Equal(WheelMode.Convert, JobKinds.ModeOf(JobKind.Convert));
        Assert.Equal(WheelMode.Tools, JobKinds.ModeOf(JobKind.Tool));
        Assert.Equal(WheelMode.Actions, JobKinds.ModeOf(JobKind.Action));
    }
}
