using Microsoft.VisualStudio.TestTools.UnitTesting;
using Remotely.Shared.Helpers;

namespace Remotely.Shared.Tests;

[TestClass]
public class PhysicalKeyMapTests
{
    [TestMethod]
    [DataRow("KeyQ", 0x10, false, 24)]
    [DataRow("KeyA", 0x1E, false, 38)]
    [DataRow("AltRight", 0x38, true, 108)]
    [DataRow("NumpadEnter", 0x1C, true, 104)]
    [DataRow("ArrowLeft", 0x4B, true, 113)]
    public void MapsPhysicalPosition(string code, int scan, bool extended, int x11)
    {
        Assert.IsTrue(PhysicalKeyMap.TryGetScanCode(code, out var actual, out var actualExtended));
        Assert.AreEqual(scan, (int)actual);
        Assert.AreEqual(extended, actualExtended);
        Assert.IsTrue(PhysicalKeyMap.TryGetX11KeyCode(code, out var actualX11));
        Assert.AreEqual(x11, (int)actualX11);
    }

    [TestMethod]
    public void RejectsUnknownCodes()
    {
        Assert.IsFalse(PhysicalKeyMap.TryGetScanCode(null, out _, out _));
        Assert.IsFalse(PhysicalKeyMap.TryGetX11KeyCode("untrusted", out _));
    }
}
