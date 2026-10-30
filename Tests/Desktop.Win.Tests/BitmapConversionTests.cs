using Microsoft.VisualStudio.TestTools.UnitTesting;
using Remotely.Desktop.Win.Services;
using System.Drawing;
using System.Drawing.Imaging;

namespace Remotely.Desktop.Win.Tests;

[TestClass]
public class BitmapConversionTests
{
    [TestMethod]
    public void PreservesColorsDimensionsAndAlpha()
    {
        using var source = new Bitmap(3, 2, PixelFormat.Format32bppArgb);
        source.SetPixel(0, 0, Color.FromArgb(255, 200, 10, 20));
        source.SetPixel(2, 1, Color.FromArgb(128, 30, 40, 50));
        using var result = ScreenCapturerWin.CopyBitmapPixels(source);
        Assert.AreEqual(3, result.Width);
        Assert.AreEqual(2, result.Height);
        var first = result.GetPixel(0, 0);
        Assert.AreEqual((byte)200, first.Red);
        Assert.AreEqual((byte)10, first.Green);
        Assert.AreEqual((byte)20, first.Blue);
        var last = result.GetPixel(2, 1);
        Assert.AreEqual((byte)128, last.Alpha);
        Assert.AreEqual((byte)30, last.Red);
        Assert.AreEqual((byte)40, last.Green);
        Assert.AreEqual((byte)50, last.Blue);
    }
}
