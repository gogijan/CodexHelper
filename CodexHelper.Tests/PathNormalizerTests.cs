using CodexHelper.Services;

namespace CodexHelper.Tests;

[TestClass]
public sealed class PathNormalizerTests
{
    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("   ")]
    public void NormalizeKey_ReturnsUnknownKeyForMissingPath(string? cwd)
    {
        Assert.AreEqual(PathNormalizer.UnknownKey, PathNormalizer.NormalizeKey(cwd));
    }

    [TestMethod]
    public void NormalizeDisplayPath_RemovesExtendedPrefixNormalizesSeparatorsAndTrimsTrailingSlash()
    {
        var normalized = PathNormalizer.NormalizeDisplayPath(@"\\?\C:/Work/CodexHelper/");

        Assert.AreEqual(@"C:\Work\CodexHelper", normalized);
    }

    [TestMethod]
    public void NormalizeKey_UsesUppercaseNormalizedDisplayPath()
    {
        var key = PathNormalizer.NormalizeKey(@"c:/Work/CodexHelper");

        Assert.AreEqual(@"C:\WORK\CODEXHELPER", key);
    }

    [TestMethod]
    public void GetProjectName_ReturnsLastPathSegmentOrUnknownName()
    {
        Assert.AreEqual("CodexHelper", PathNormalizer.GetProjectName(@"C:\Work\CodexHelper\", "Unknown"));
        Assert.AreEqual("Unknown", PathNormalizer.GetProjectName(null, "Unknown"));
    }
}
