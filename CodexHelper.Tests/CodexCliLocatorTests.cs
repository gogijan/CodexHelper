using CodexHelper.Services;

namespace CodexHelper.Tests;

[TestClass]
public sealed class CodexCliLocatorTests
{
    [TestMethod]
    [DataRow(@"C:\Tools\codex.cmd", "cmd.exe", @"/d /c """"C:\Tools\codex.cmd"" app-server --listen stdio://""")]
    [DataRow(@"C:\Tools\codex.bat", "cmd.exe", @"/d /c """"C:\Tools\codex.bat"" app-server --listen stdio://""")]
    [DataRow(@"C:\Tools\codex.ps1", "powershell.exe", @"-NoLogo -NoProfile -ExecutionPolicy Bypass -File ""C:\Tools\codex.ps1"" app-server --listen stdio://")]
    [DataRow(@"C:\Tools\codex.exe", @"C:\Tools\codex.exe", "app-server --listen stdio://")]
    public void BuildCommand_ReturnsExpectedLauncherForCandidateType(
        string candidate,
        string expectedFileName,
        string expectedArguments)
    {
        var command = CodexCliLocator.BuildCommand(candidate, CodexCliLocator.AppServerArguments);

        Assert.AreEqual(expectedFileName, command.FileName);
        Assert.AreEqual(expectedArguments, command.Arguments);
    }

    [TestMethod]
    public void CreateStartInfo_ConfiguresRedirectedUtf8Process()
    {
        var startInfo = CodexCliLocator.CreateStartInfo(
            new CodexCliCommand("codex", CodexCliLocator.AppServerArguments),
            redirectStandardInput: true);

        Assert.AreEqual("codex", startInfo.FileName);
        Assert.AreEqual(CodexCliLocator.AppServerArguments, startInfo.Arguments);
        Assert.IsTrue(startInfo.RedirectStandardInput);
        Assert.IsTrue(startInfo.RedirectStandardOutput);
        Assert.IsTrue(startInfo.RedirectStandardError);
        Assert.IsFalse(startInfo.UseShellExecute);
        Assert.IsTrue(startInfo.CreateNoWindow);
        Assert.AreEqual("utf-8", startInfo.StandardInputEncoding?.WebName);
        Assert.AreEqual("utf-8", startInfo.StandardOutputEncoding?.WebName);
        Assert.AreEqual("utf-8", startInfo.StandardErrorEncoding?.WebName);
    }

    [TestMethod]
    public void EnumeratePathCandidates_ReturnsExistingCandidatesOnceInSearchOrder()
    {
        using var temp = TempDirectory.Create();
        var firstDirectory = Path.Combine(temp.DirectoryPath, "first");
        var secondDirectory = Path.Combine(temp.DirectoryPath, "second");
        Directory.CreateDirectory(firstDirectory);
        Directory.CreateDirectory(secondDirectory);

        var cmdPath = Path.Combine(firstDirectory, "codex.cmd");
        var exePath = Path.Combine(firstDirectory, "codex.exe");
        var ps1Path = Path.Combine(secondDirectory, "codex.ps1");
        File.WriteAllText(cmdPath, string.Empty);
        File.WriteAllText(exePath, string.Empty);
        File.WriteAllText(ps1Path, string.Empty);

        var pathValue = string.Join(
            Path.PathSeparator,
            firstDirectory,
            firstDirectory,
            secondDirectory);

        var candidates = CodexCliLocator.EnumeratePathCandidates(pathValue).ToArray();

        CollectionAssert.AreEqual(new[] { cmdPath, exePath, ps1Path }, candidates);
    }
}
