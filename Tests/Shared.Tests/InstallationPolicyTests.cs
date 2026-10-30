using Microsoft.VisualStudio.TestTools.UnitTesting;
using Remotely.Shared.Helpers;
using System;
using System.IO;
using System.IO.Compression;

namespace Remotely.Shared.Tests;

[TestClass]
public class InstallationPolicyTests
{
    [TestMethod]
    public void ExistingConfigurationIsPreservedByteForByte()
    {
        var legacy = "{ \"DeviceID\":\"existing-device\", \"Host\":\"https://support.example.com/\", \"OrganizationID\":\"org-1\", \"ServerVerificationToken\":\"existing-token\", \"FutureSetting\":{\"Keep\":true} }";
        Assert.AreEqual(legacy, InstallationPolicy.MergeConfiguration(legacy, "https://support.example.com", "org-1"));
    }

    [TestMethod]
    [DataRow("https://other.example.com", "org-1")]
    [DataRow("https://support.example.com", "org-2")]
    public void MigrationRejectsChangingServerOrOrganization(string server, string organization)
    {
        var legacy = "{\"DeviceID\":\"existing\",\"Host\":\"https://support.example.com\",\"OrganizationID\":\"org-1\"}";
        Assert.ThrowsException<InvalidOperationException>(() => InstallationPolicy.MergeConfiguration(legacy, server, organization));
    }

    [TestMethod]
    [DataRow("http://support.example.com")]
    [DataRow("https://user:secret@support.example.com")]
    [DataRow("https://support.example.com/path")]
    [DataRow("https://support.example.com/?x=y")]
    public void RejectsUnsafeServerOrigins(string server) => Assert.ThrowsException<InvalidOperationException>(() => InstallationPolicy.NormalizeServer(server));

    [TestMethod]
    [DataRow("../outside.txt")]
    [DataRow("/absolute.txt")]
    [DataRow("C:/outside.txt")]
    [DataRow("ConnectionInfo.json")]
    public void PackageCannotEscapeOrReplaceConfiguration(string name)
    {
        var directory = Path.Combine(Path.GetTempPath(), "remotely-package-test-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        try
        {
            var zipPath = Path.Combine(directory, "input.zip");
            using(var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
            using(var writer = new StreamWriter(zip.CreateEntry(name).Open())) writer.Write("payload");
            Assert.ThrowsException<InvalidDataException>(() => InstallationPolicy.ExtractPackage(zipPath, Path.Combine(directory, "stage")));
        }
        finally { Directory.Delete(directory, true); }
    }
}
