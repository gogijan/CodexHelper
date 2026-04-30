using CodexHelper.Services;

namespace CodexHelper.Tests;

[TestClass]
public sealed class CodexAppServerClientTests
{
    [TestMethod]
    public async Task ListReadAndArchive_UseSingleOutputPumpProcess()
    {
        using var temp = TempDirectory.Create();
        var scriptPath = WriteFakeServerScript(temp.DirectoryPath);
        var commandRequests = 0;
        await using var client = CreateClient(scriptPath, "normal", () => commandRequests++);

        var threads = await client.ListThreadsAsync(archived: false);
        var readResult = await client.ReadThreadAsync("thread-1");
        await client.ArchiveThreadAsync("thread-1");

        Assert.AreEqual(1, commandRequests);
        Assert.AreEqual(1, threads.Count);
        Assert.AreEqual("thread-1", threads[0].Id);
        Assert.AreEqual("Thread one", threads[0].Name);
        Assert.AreEqual(@"C:\Work\CodexHelper", threads[0].Cwd);

        var messages = ConversationParser.ParseThreadReadResult(readResult);
        Assert.AreEqual(1, messages.Count);
        Assert.AreEqual("hello from app-server", messages[0].Content);
    }

    [TestMethod]
    public async Task TimedOutRequest_RestartsProcessAndAllowsNextRequest()
    {
        using var temp = TempDirectory.Create();
        var scriptPath = WriteFakeServerScript(temp.DirectoryPath);
        var commandRequests = 0;
        await using var client = CreateClient(
            scriptPath,
            "hang-read",
            () => commandRequests++,
            TimeSpan.FromMilliseconds(800));

        var timedOut = false;
        try
        {
            await client.ReadThreadAsync("thread-1");
        }
        catch (AppServerException)
        {
            timedOut = true;
        }

        var threads = await client.ListThreadsAsync(archived: false);

        Assert.IsTrue(timedOut);
        Assert.IsTrue(commandRequests >= 2);
        Assert.AreEqual(1, threads.Count);
        Assert.AreEqual("thread-1", threads[0].Id);
    }

    private static CodexAppServerClient CreateClient(
        string scriptPath,
        string mode,
        Action commandRequested,
        TimeSpan? requestTimeout = null)
    {
        return new CodexAppServerClient(
            _ =>
            {
                commandRequested();
                return Task.FromResult(new CodexCliCommand(
                    "powershell.exe",
                    $"-NoLogo -NoProfile -ExecutionPolicy Bypass -File {QuotePowerShellArgument(scriptPath)} {mode}"));
            },
            command => CodexCliLocator.CreateStartInfo(command, redirectStandardInput: true),
            requestTimeout ?? TimeSpan.FromSeconds(5));
    }

    private static string WriteFakeServerScript(string directoryPath)
    {
        var scriptPath = Path.Combine(directoryPath, "fake-codex-app-server.ps1");
        File.WriteAllText(scriptPath, FakeServerScript);
        return scriptPath;
    }

    private static string QuotePowerShellArgument(string value)
    {
        return $"\"{value.Replace("`", "``", StringComparison.Ordinal).Replace("\"", "`\"", StringComparison.Ordinal)}\"";
    }

    private const string FakeServerScript = """
$mode = $args[0]

function Send-Json($value) {
    $json = $value | ConvertTo-Json -Compress -Depth 20
    [Console]::Out.WriteLine($json)
    [Console]::Out.Flush()
}

while ($true) {
    $line = [Console]::In.ReadLine()
    if ($null -eq $line) {
        break
    }

    if ([string]::IsNullOrWhiteSpace($line)) {
        continue
    }

    $request = $line | ConvertFrom-Json
    if ($null -eq $request.id) {
        continue
    }

    if ($request.method -eq 'initialize') {
        Send-Json @{ id = $request.id; result = @{} }
        continue
    }

    if ($request.method -eq 'thread/list') {
        Send-Json @{
            id = $request.id
            result = @{
                data = @(
                    @{
                        id = 'thread-1'
                        name = 'Thread one'
                        cwd = 'C:\Work\CodexHelper'
                        path = 'C:\Users\tester\.codex\sessions\rollout-thread-1.jsonl'
                        updatedAt = 1770000000
                    }
                )
                nextCursor = $null
            }
        }
        continue
    }

    if ($request.method -eq 'thread/read') {
        if ($mode -eq 'hang-read') {
            Start-Sleep -Seconds 10
            continue
        }

        Send-Json @{
            id = $request.id
            result = @{
                thread = @{
                    messages = @(
                        @{
                            role = 'user'
                            content = 'hello from app-server'
                        }
                    )
                }
            }
        }
        continue
    }

    if ($request.method -eq 'thread/archive' -or $request.method -eq 'thread/unarchive') {
        Send-Json @{ id = $request.id; result = @{} }
        continue
    }

    Send-Json @{
        id = $request.id
        error = @{
            code = -32601
            message = "Unknown method $($request.method)"
        }
    }
}
""";
}
