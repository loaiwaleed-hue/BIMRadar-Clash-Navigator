using System.Drawing;
using BIMRadar.ClashNavigator;
using Xunit;

// #97
public class SnapshotCropTests
{
    static Bitmap White(int w, int h, params Rectangle[] blacks)
    {
        var b = new Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(b))
        {
            g.Clear(Color.White);
            foreach (var r in blacks) g.FillRectangle(Brushes.Black, r);
        }
        return b;
    }

    [Fact]
    public void ContentRect_IsExact()
    {
        using (var b = White(1000, 800, new Rectangle(100, 600, 50, 40)))
            Assert.Equal(new Rectangle(100, 600, 50, 40), SnapshotCrop.ContentRect(b));
    }

    [Fact]
    public void ContentRect_AllWhite_IsEmpty()
    {
        using (var b = White(1000, 800))
            Assert.Equal(Rectangle.Empty, SnapshotCrop.ContentRect(b));
    }

    [Fact]
    public void FitCrop_HasAspectAndContainsContent()
    {
        var content = new Rectangle(400, 300, 50, 40);
        var r = SnapshotCrop.FitCrop(content, new Size(1000, 800), 1.5);
        Assert.True(r.Contains(content));
        Assert.InRange((double)r.Width / r.Height, 1.49, 1.51);
    }

    [Fact]
    public void FitCrop_NearEdge_StaysInsideImage()
    {
        var img = new Size(1000, 800);
        var r = SnapshotCrop.FitCrop(new Rectangle(100, 600, 50, 40), img, 1.5);
        Assert.True(new Rectangle(Point.Empty, img).Contains(r));
        Assert.True(r.Contains(new Rectangle(100, 600, 50, 40)));
        r = SnapshotCrop.FitCrop(new Rectangle(0, 0, 30, 30), img, 1.0);
        Assert.True(r.X >= 0 && r.Y >= 0);
    }

    [Fact]
    public void FitCrop_TooLarge_ReturnsWholeImage()
    {
        var img = new Size(1000, 800);
        Assert.Equal(new Rectangle(0, 0, 1000, 800), SnapshotCrop.FitCrop(new Rectangle(0, 0, 1000, 800), img, 1.5));
    }
}
