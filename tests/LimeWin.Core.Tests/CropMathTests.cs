using LimeWin.Core.Tools;

namespace LimeWin.Core.Tests;

public class CropMathTests
{
    const int W = 400, H = 300;

    [Fact]
    public void Full_IsTheWholeImage() =>
        Assert.Equal(new PixelRect(0, 0, W, H), CropMath.Full(W, H));

    [Fact]
    public void Move_ShiftsAndStopsAtTheEdges()
    {
        var r = new PixelRect(100, 100, 50, 40);
        Assert.Equal(new PixelRect(120, 90, 50, 40), CropMath.Move(r, 20.4, -10.2, W, H));
        Assert.Equal(new PixelRect(0, 0, 50, 40), CropMath.Move(r, -500, -500, W, H));
        Assert.Equal(new PixelRect(W - 50, H - 40, 50, 40), CropMath.Move(r, 500, 500, W, H));
    }

    [Fact]
    public void Resize_Free_MovesOnlyTheDraggedEdges()
    {
        var r = new PixelRect(100, 100, 100, 100);
        Assert.Equal(new PixelRect(100, 100, 130, 100), CropMath.Resize(r, CropHandle.Right, 30, 99, null, W, H));
        Assert.Equal(new PixelRect(80, 90, 120, 110), CropMath.Resize(r, CropHandle.TopLeft, -20, -10, null, W, H));
        Assert.Equal(new PixelRect(100, 100, 100, 150), CropMath.Resize(r, CropHandle.Bottom, 0, 50, null, W, H));
    }

    [Fact]
    public void Resize_Free_ClampsToTheImage()
    {
        var r = new PixelRect(100, 100, 100, 100);
        Assert.Equal(new PixelRect(0, 100, 200, 100), CropMath.Resize(r, CropHandle.Left, -1000, 0, null, W, H));
        Assert.Equal(new PixelRect(100, 100, 300, 200), CropMath.Resize(r, CropHandle.BottomRight, 1000, 1000, null, W, H));
    }

    [Fact]
    public void Resize_Free_NeverGoesBelowOnePixelOrFlips()
    {
        var r = new PixelRect(100, 100, 100, 100);
        Assert.Equal(new PixelRect(199, 100, 1, 100), CropMath.Resize(r, CropHandle.Left, 500, 0, null, W, H));
        Assert.Equal(new PixelRect(100, 100, 1, 1), CropMath.Resize(r, CropHandle.BottomRight, -500, -500, null, W, H));
    }

    [Fact]
    public void Resize_Corner_WithAspect_KeepsTheRatioAndTheOppositeCorner()
    {
        var r = new PixelRect(100, 100, 160, 90); // 16:9
        var got = CropMath.Resize(r, CropHandle.BottomRight, 40, 0, 16.0 / 9, W, H);
        Assert.Equal(new PixelRect(100, 100, 200, 113), got);

        var tl = CropMath.Resize(r, CropHandle.TopLeft, -32, -10, 16.0 / 9, W, H);
        Assert.Equal(260, tl.X + tl.Width);
        Assert.Equal(190, tl.Y + tl.Height);
        Assert.Equal(192, tl.Width);
        Assert.Equal(108, tl.Height);
    }

    [Fact]
    public void Resize_Corner_WithAspect_StopsAtTheImageEdge()
    {
        var r = new PixelRect(100, 100, 100, 100);
        var got = CropMath.Resize(r, CropHandle.BottomRight, 1000, 1000, 1, W, H);
        Assert.Equal(new PixelRect(100, 100, 200, 200), got); // the bottom edge (300) stops it first
    }

    [Fact]
    public void Resize_Edge_WithAspect_GrowsTheOtherSideAroundTheCentre()
    {
        var r = new PixelRect(100, 100, 100, 100);
        var got = CropMath.Resize(r, CropHandle.Right, 20, 0, 1, W, H);
        Assert.Equal(new PixelRect(100, 90, 120, 120), got);
    }

    [Fact]
    public void Resize_Edge_WithAspect_ShiftsInsideWhenTheCentreIsNearAnEdge()
    {
        var r = new PixelRect(0, 0, 100, 100);
        var got = CropMath.Resize(r, CropHandle.Right, 50, 0, 1, W, H);
        Assert.Equal(new PixelRect(0, 0, 150, 150), got);
    }

    [Theory]
    [InlineData(1.0, 50, 0, 300, 300)]      // square inside the 400x300 selection, centred
    [InlineData(16.0 / 9, 0, 38, 400, 225)]
    [InlineData(9.0 / 16, 116, 0, 169, 300)]
    public void FitAspect_IsTheLargestRatioRectInsideTheSelection(double aspect, int x, int y, int w, int h)
    {
        var got = CropMath.FitAspect(CropMath.Full(W, H), aspect, W, H);
        Assert.Equal(h, got.Height);
        Assert.Equal(w, got.Width);
        Assert.Equal(y, got.Y);
        Assert.InRange(got.X, x - 1, x + 1);
    }

    [Fact]
    public void FitAspect_Free_LeavesTheSelectionAlone()
    {
        var r = new PixelRect(10, 20, 30, 40);
        Assert.Equal(r, CropMath.FitAspect(r, null, W, H));
    }

    [Fact]
    public void FromField_XAndY_StayInside()
    {
        var r = new PixelRect(100, 100, 100, 100);
        Assert.Equal(new PixelRect(300, 100, 100, 100), CropMath.FromField(r, CropField.X, 999, null, W, H));
        Assert.Equal(new PixelRect(100, 0, 100, 100), CropMath.FromField(r, CropField.Y, -5, null, W, H));
    }

    [Fact]
    public void FromField_Size_ClampsToTheImageAndOnePixel()
    {
        var r = new PixelRect(100, 100, 100, 100);
        Assert.Equal(new PixelRect(100, 100, 300, 100), CropMath.FromField(r, CropField.Width, 999, null, W, H));
        Assert.Equal(new PixelRect(100, 100, 100, 1), CropMath.FromField(r, CropField.Height, 0, null, W, H));
    }

    [Fact]
    public void FromField_WithAspect_UpdatesTheOtherSide()
    {
        var r = new PixelRect(0, 0, 160, 90);
        Assert.Equal(new PixelRect(0, 0, 320, 180), CropMath.FromField(r, CropField.Width, 320, 16.0 / 9, W, H));
        Assert.Equal(new PixelRect(0, 0, 80, 45), CropMath.FromField(r, CropField.Height, 45, 16.0 / 9, W, H));
    }

    [Fact]
    public void FromField_WithAspect_ShrinksBothWhenTheOtherSideWouldLeaveTheImage()
    {
        var r = new PixelRect(0, 100, 100, 100);
        var got = CropMath.FromField(r, CropField.Width, 400, 1, W, H); // only 200 px of height below y=100
        Assert.Equal(new PixelRect(0, 100, 200, 200), got);
    }

    [Fact]
    public void Clamp_FixesAnOutOfRangeRect()
    {
        Assert.Equal(new PixelRect(0, 0, W, H), CropMath.Clamp(new PixelRect(-10, -10, 9999, 9999), W, H));
        Assert.Equal(new PixelRect(W - 1, H - 1, 1, 1), CropMath.Clamp(new PixelRect(W + 5, H + 5, 0, 0), W, H));
    }
}
