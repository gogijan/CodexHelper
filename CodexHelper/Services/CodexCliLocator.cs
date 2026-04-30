using System.Diagnostics;
using System.IO;
using System.Text;

namespace CodexHelper.Services;

public sealed class CodexCliLocator
{
    public const string AppServerArguments = "app-server --listen stdio://";
    public const string AppServerHelpArguments = "app-server --help";

    private static readonly TimeSpan StartupProbeTimeout = TimeSpan.FromSeconds(8);

    private string? _appServerCommandPath;

    public async Task<CodexCliCommand> FindAppServerCommandAsync(CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(_appServerCommandPath))
        {
            return BuildCommand(_appServerCommandPath, AppServerArguments);
        }

        var hasCandidates = false;
        StartupProbeResult? lastFailedProbe = null;

        foreach (var candidate in EnumeratePathCandidates())
        {
            hasCandidates = true;
            var command = BuildCommand(candidate, AppServerHelpArguments);
            StartupProbeResult result;
            try
            {
                result = await RunStartupProbeAsync(command, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (AppServerException ex)
            {
                lastFailedProbe = new StartupProbeResult(-1, string.Empty, ex.Message);
                DiagnosticLogService.Warning($"Codex CLI probe could not start for '{candidate}'.", ex);
                continue;
            }

            if (result.ExitCode == 0)
            {
                _appServerCommandPath = candidate;
                DiagnosticLogService.Info($"Using Codex CLI at '{candidate}'.");
                return BuildCommand(candidate, AppServerArguments);
            }

            lastFailedProbe = result;
            DiagnosticLogService.Warning($"Codex CLI probe failed for '{candidate}': {BuildStartupProbeFailureMessage(result)}");
        }

        if (!hasCandidates)
        {
            DiagnosticLogService.Warning("Codex CLI was not found on PATH.");
            throw new CodexCliMissingException();
        }

        throw new CodexAppServerUnsupportedException(
            lastFailedProbe is null
                ? "`codex app-server --help` failed."
                : BuildStartupProbeFailureMessage(lastFailedProbe));
    }

    public static CodexCliCommand BuildCommand(string candidate, string codexArguments)
    {
        var extension = Path.GetExtension(candidate);
        if (extension.Equals(".ps1", StringComparison.OrdinalIgnoreCase))
        {
            return new CodexCliCommand(
                "powershell.exe",
                $"-NoLogo -NoProfile -ExecutionPolicy Bypass -File \"{candidate}\" {codexArguments}");
        }

        if (extension.Equals(".cmd", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".bat", StringComparison.OrdinalIgnoreCase))
        {
            return new CodexCliCommand("cmd.exe", $"/d /c \"\"{candidate}\" {codexArguments}\"");
        }

        return new CodexCliCommand(candidate, codexArguments);
    }

    public static ProcessStartInfo CreateStartInfo(CodexCliCommand command, bool redirectStandardInput)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = command.FileName,
            Arguments = command.Arguments,
            RedirectStandardInput = redirectStandardInput,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = new UTF8Encoding(false),
            StandardErrorEncoding = new UTF8Encoding(false)
        };

        if (redirectStandardInput)
        {
            startInfo.StandardInputEncoding = new UTF8Encoding(false);
        }

        return startInfo;
    }

    public static IEnumerable<string> EnumeratePathCandidates(string? pathValue = null)
    {
        var path = pathValue ?? Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        string[] names = OperatingSystem.IsWindows()
            ? ["codex.cmd", "codex.exe", "codex.bat", "codex.ps1"]
            : ["codex"];

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            foreach (var name in names)
            {
                string candidate;
                try
                {
                    candidate = Path.Combine(directory, name);
                }
                catch (ArgumentException)
                {
                    continue;
                }

                if (File.Exists(candidate) && seen.Add(candidate))
                {
                    yield return candidate;
                }
            }
        }
    }

    private static async Task<StartupProbeResult> RunStartupProbeAsync(
        CodexCliCommand command,
        CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = CreateStartInfo(command, redirectStandardInput: false),
            EnableRaisingEvents = true
        };

        try
        {
            if (!process.Start())
            {
                throw new AppServerException("Could not start Codex CLI to check app-server support.");
            }
        }
        catch (Exception ex) when (ex is not AppServerException)
        {
            throw new AppServerException("Could not start Codex CLI to check app-server support.", ex);
        }

        var standardOutputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var standardErrorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        var exitTask = process.WaitForExitAsync(cancellationToken);
        var timeoutTask = Task.Delay(StartupProbeTimeout, cancellationToken);
        if (await Task.WhenAny(exitTask, timeoutTask) != exitTask)
        {
            try
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(CancellationToken.None);
            }
            catch
            {
            }

            cancellationToken.ThrowIfCancellationRequested();
            throw new CodexAppServerUnsupportedException("Timed out checking `codex app-server --help`.");
        }

        await exitTask;
        return new StartupProbeResult(
            process.ExitCode,
            await standardOutputTask,
            await standardErrorTask);
    }

    private static string BuildStartupProbeFailureMessage(StartupProbeResult result)
    {
        var details = FirstNonEmpty(result.StandardError, result.StandardOutput);
        return string.IsNullOrWhiteSpace(details)
            ? "`codex app-server --help` failed."
            : $"`codex app-server --help` failed: {details.Trim()}";
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;
    }

    private sealed record StartupProbeResult(
        int ExitCode,
        string StandardOutput,
        string StandardError);
}

public sealed record CodexCliCommand(string FileName, string Arguments);
