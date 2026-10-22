using LimeWin.Core.Wheel;

namespace LimeWin.Core.Tests;

public class WheelGeometryTests
{
    [Theory] [InlineData(0, -90, 4, 0)] [InlineData(90, 0, 4, 1)] [InlineData(0, 90, 4, 2)] [InlineData(-90, 0, 4, 3)]
    public void PetalAt_CardinalPoints(double dx, double dy, int count, int expected) => Assert.Equal(expected, new WheelGeometry().PetalAt(dx, dy, count));
    [Fact] public void PetalAt_Hole_IsNull() => Assert.Null(new WheelGeometry().PetalAt(0, -30, 4));
    [Fact] public void PetalAt_Outside_IsNull() => Assert.Null(new WheelGeometry().PetalAt(0, -140, 4));
    [Fact] public void PetalAt_Gutter_IsNull() => Assert.Null(new WheelGeometry().PetalAt(Math.Sin(Math.PI / 4) * 90, -Math.Cos(Math.PI / 4) * 90, 4)); // 0번과 1번 경계
    [Fact] public void PetalAt_SinglePetal_WholeRing() => Assert.Equal(0, new WheelGeometry().PetalAt(0, 90, 1));

    [Fact] public void PetalAt_JustPastGutter_BelongsToNeighbour()
    {
        var a = Math.PI / 4 + 0.05; // 경계에서 약 2.9도, 반지름 90의 반-간격(약 1.6도)보다 큼
        Assert.Equal(1, new WheelGeometry().PetalAt(Math.Sin(a) * 90, -Math.Cos(a) * 90, 4));
    }
    [Fact] public void PetalAt_SinglePetal_StillRespectsRadius() { Assert.Null(new WheelGeometry().PetalAt(0, 30, 1)); Assert.Null(new WheelGeometry().PetalAt(0, 140, 1)); }
    [Fact] public void PetalAt_ZeroCount_IsNull() => Assert.Null(new WheelGeometry().PetalAt(0, -90, 0));
    [Fact] public void PetalAt_RadiusBounds_AreInclusive() { var g = new WheelGeometry(); Assert.Equal(0, g.PetalAt(0, -WheelGeometry.Inner, 4)); Assert.Equal(0, g.PetalAt(0, -WheelGeometry.Outer, 4)); }

    [Fact] public void BearingOf_IsClockwiseFromTwelve()
    {
        var g = new WheelGeometry();
        Assert.Equal(0, g.BearingOf(0, 4), 9);
        Assert.Equal(Math.PI / 2, g.BearingOf(1, 4), 9);
        Assert.Equal(Math.PI, g.BearingOf(2, 4), 9);
    }
}
