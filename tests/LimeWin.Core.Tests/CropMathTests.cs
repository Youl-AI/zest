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

    [Fact]
    public void Resize_TopAndBottom_WithAspect_GrowTheWidthAroundTheCentre()
    {
        var r = new PixelRect(100, 100, 100, 100);
        Assert.Equal(new PixelRect(90, 100, 120, 120), CropMath.Resize(r, CropHandle.Bottom, 0, 20, 1, W, H));
        Assert.Equal(new PixelRect(90, 80, 120, 120), CropMath.Resize(r, CropHandle.Top, 0, -20, 1, W, H));
    }

    [Fact]
    public void Resize_Left_WithAspect_GrowsLeftward()
    {
        var r = new PixelRect(200, 100, 100, 100);
        Assert.Equal(new PixelRect(180, 90, 120, 120), CropMath.Resize(r, CropHandle.Left, -20, 0, 1, W, H));
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(4.0 / 3)]
    [InlineData(3.0 / 2)]
    [InlineData(16.0 / 9)]
    [InlineData(9.0 / 16)]
    public void Resize_WithAspect_NeverMovesTheAnchoredEdges(double aspect)
    {
        var r = CropMath.FitAspect(new PixelRect(101, 77, 163, 111), aspect, W, H);
        int left = r.X, top = r.Y, right = r.X + r.Width, bottom = r.Y + r.Height;
        for (var d = -60.0; d <= 60; d += 0.7)
        {
            var l = CropMath.Resize(r, CropHandle.Left, d, 0, aspect, W, H);
            Assert.Equal(right, l.X + l.Width);
            var t = CropMath.Resize(r, CropHandle.Top, 0, d, aspect, W, H);
            Assert.Equal(bottom, t.Y + t.Height);
            var rr = CropMath.Resize(r, CropHandle.Right, d, 0, aspect, W, H);
            Assert.Equal(left, rr.X);
            var b = CropMath.Resize(r, CropHandle.Bottom, 0, d, aspect, W, H);
            Assert.Equal(top, b.Y);

            var tl = CropMath.Resize(r, CropHandle.TopLeft, d, d, aspect, W, H);
            Assert.Equal((right, bottom), (tl.X + tl.Width, tl.Y + tl.Height));
            var tr = CropMath.Resize(r, CropHandle.TopRight, -d, d, aspect, W, H);
            Assert.Equal((left, bottom), (tr.X, tr.Y + tr.Height));
            var bl = CropMath.Resize(r, CropHandle.BottomLeft, d, -d, aspect, W, H);
            Assert.Equal((right, top), (bl.X + bl.Width, bl.Y));
            var br = CropMath.Resize(r, CropHandle.BottomRight, -d, -d, aspect, W, H);
            Assert.Equal((left, top), (br.X, br.Y));

            foreach (var got in new[] { l, t, rr, b, tl, tr, bl, br })
            {
                if (got.Width >= 20 && got.Height >= 20) // a few pixels cannot hold 9:16 exactly
                    Assert.InRange(got.Width / (double)got.Height, aspect * 0.9, aspect * 1.1);
                Assert.Equal(got, CropMath.Clamp(got, W, H)); // inside the image
            }
        }
    }

    [Fact]
    public void Resize_TopRightAndBottomLeft_WithAspect_KeepTheOppositeCorner()
    {
        var r = new PixelRect(100, 100, 100, 100);
        Assert.Equal(new PixelRect(100, 70, 130, 130), CropMath.Resize(r, CropHandle.TopRight, 30, -10, 1, W, H));
        Assert.Equal(new PixelRect(60, 100, 140, 140), CropMath.Resize(r, CropHandle.BottomLeft, -40, 10, 1, W, H));
    }

    [Fact]
    public void Resize_Side_WithAspect_IsCappedByTheOtherDimension()
    {
        // Width could reach 400, but height (300) caps a square at 300 and the centre shifts to fit.
        Assert.Equal(new PixelRect(0, 0, 300, 300), CropMath.Resize(new PixelRect(0, 100, 50, 50), CropHandle.Right, 1000, 0, 1, W, H));
        // 2:1 has room for the full width: 400 x 200, centred on y=125 and shifted inside.
        Assert.Equal(new PixelRect(0, 25, 400, 200), CropMath.Resize(new PixelRect(0, 100, 100, 50), CropHandle.Right, 1000, 0, 2, W, H));
        // Bottom handle, 2:1: height could reach 300, but the image width (400) caps it at 200; the centre shifts to fit.
        Assert.Equal(new PixelRect(0, 0, 400, 200), CropMath.Resize(new PixelRect(150, 0, 100, 50), CropHandle.Bottom, 0, 1000, 2, W, H));
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
