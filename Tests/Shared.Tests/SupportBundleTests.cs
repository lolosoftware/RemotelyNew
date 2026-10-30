using Microsoft.VisualStudio.TestTools.UnitTesting;
using Remotely.Shared.Models;
using Remotely.Shared.Services;
using System;
using System.IO;
using System.Text.Json;

namespace Remotely.Shared.Tests;

[TestClass]
public class SupportBundleTests
{
    [TestMethod]
    public void UnpackedPortableClientReadsServerAndOrganization()
    {
        var directory = Path.Combine(Path.GetTempPath(), "remotely-support-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(Path.Combine(directory,"ServerData.json"), JsonSerializer.Serialize(new EmbeddedServerData(new Uri("https://support.example.com"), "org-1")));
            var result = new EmbeddedServerDataProvider().TryGetEmbeddedData(Path.Combine(directory,"Remotely_Desktop.exe"));
            Assert.IsTrue(result.IsSuccess);
            Assert.AreEqual("org-1", result.Value.OrganizationId);
            Assert.AreEqual("https://support.example.com/", result.Value.ServerUrl.AbsoluteUri);
        }
        finally { Directory.Delete(directory,true); }
    }

    [TestMethod]
    public void LegacyEncodedExecutableNamesRemainCompatible()
    {
        var provider = new EmbeddedServerDataProvider();
        var name = provider.GetEncodedFileName("Remotely_Desktop.exe", new EmbeddedServerData(new Uri("https://legacy.example.com"), "legacy-org"));
        var result = provider.TryGetEmbeddedData(name);
        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual("legacy-org", result.Value.OrganizationId);
    }
}
