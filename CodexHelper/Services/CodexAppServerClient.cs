using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CodexHelper.Models;

namespace CodexHelper.Services;

public sealed class CodexAppServerClient : IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly SemaphoreSlim _requestLock = new(1, 1);
    private readonly TimeSpan _requestTimeout = TimeSpan.FromSeconds(45);
    private readonly StringBuilder _stderr = new();

    private Process? _process;
    private Task? _stderrPump;
    private int _nextRequestId = 1;
    private bool _initialized;

    public async Task<IReadOnlyList<CodexThread>> ListThreadsAsync(bool archived, CancellationToken cancellationToken = default)
    {
        var threads = new List<CodexThread>();
        string? cursor = null;

        do
        {
            var parameters = new Dictionary<string, object?>
            {
                ["limit"] = 100,
                ["sortKey"] = "updated_at",
                ["sortDirection"] = "desc",
                ["archived"] = archived
            };

            if (!string.IsNullOrWhiteSpace(cursor))
            {
                parameters["cursor"] = cursor;
            }

            var result = await SendRequestAsync("thread/list", parameters, cancellationToken);
            if (result.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in data.EnumerateArray())
                {
                    if (!TryGetString(item, "id", out var id) || string.IsNullOrWhiteSpace(id))
                    {
                        continue;
                    }

                    var cwd = TryGetString(item, "cwd", out var itemCwd) ? itemCwd : null;
                    var path = TryGetString(item, "path", out var itemPath) ? itemPath : null;

                    threads.Add(new CodexThread
                    {
                        Id = id,
                        Name = TryGetString(item, "name", out var name) ? name : string.Empty,
                        Cwd = cwd,
                        Path = path,
                        UpdatedAt = TryGetUnixTime(item, "updatedAt"),
                        IsArchived = archived,
                        IsChat = IsChatCwd(cwd)
                    });
                }
            }

            cursor = TryGetString(result, "nextCursor", out var nextCursor) ? nextCursor : null;
        }
        while (!string.IsNullOrWhiteSpace(cursor));

        return threads;
    }

    public Task<JsonElement> ReadThreadAsync(string threadId, CancellationToken cancellationToken = default)
    {
        return SendRequestAsync(
            "thread/read",
            new Dictionary<string, object?>
            {
                ["threadId"] = threadId,
                ["includeTurns"] = true
            },
            cancellationToken);
    }

    public Task ArchiveThreadAsync(string threadId, CancellationToken cancellationToken = default)
    {
        return SendCommandAsync("thread/archive", threadId, cancellationToken);
    }

    public Task UnarchiveThreadAsync(string threadId, CancellationToken cancellationToken = default)
    {
        return SendCommandAsync("thread/unarchive", threadId, cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_process is { HasExited: false })
            {
                try
                {
                    _process.StandardInput.Close();
                }
                catch
                {
                }

                if (!_process.WaitForExit(3000))
                {
                    _process.Kill(entireProcessTree: true);
                    await _process.WaitForExitAsync();
                }
            }
        }
        finally
        {
            _process?.Dispose();
            _requestLock.Dispose();
        }
    }

    private async Task SendCommandAsync(string method, string threadId, CancellationToken cancellationToken)
    {
        _ = await SendRequestAsync(
            method,
            new Dictionary<string, object?> { ["threadId"] = threadId },
            cancellationToken);
    }

    private async Task<JsonElement> SendRequestAsync(string method, object? parameters, CancellationToken cancellationToken)
    {
        await _requestLock.WaitAsync(cancellationToken);
        try
        {
            await EnsureStartedAsync(cancellationToken);
            return await SendRequestCoreAsync(method, parameters, cancellationToken);
        }
        finally
        {
            _requestLock.Release();
        }
    }

    private async Task EnsureStartedAsync(CancellationToken cancellationToken)
    {
        if (_initialized && _process is { HasExited: false })
        {
            return;
        }

        StartProcess();

        _ = await SendRequestCoreAsync(
            "initialize",
            new
            {
                clientInfo = new
                {
                    name = "codex_helper",
                    title = "CodexHelper",
                    version = "0.1.0"
                },
                capabilities = new
                {
                    experimentalApi = true
                }
            },
            cancellationToken);

        await SendNotificationAsync("initialized", cancellationToken);
        _initialized = true;
    }

    private void StartProcess()
    {
        if (_process is { HasExited: false })
        {
            return;
        }

        var command = ResolveCodexCommand();
        var startInfo = new ProcessStartInfo
        {
            FileName = command.FileName,
            Arguments = command.Arguments,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardInputEncoding = new UTF8Encoding(false),
            StandardOutputEncoding = new UTF8Encoding(false),
            StandardErrorEncoding = new UTF8Encoding(false)
        };

        _process = new Process
        {
            StartInfo = startInfo,
            EnableRaisingEvents = true
        };

        try
        {
            if (!_process.Start())
            {
                throw new AppServerException("Could not start codex app-server.");
            }
        }
        catch (Exception ex) when (ex is not AppServerException)
        {
            throw new AppServerException("Could not start codex app-server.", ex);
        }

        _stderr.Clear();
        _stderrPump = Task.Run(async () =>
        {
            while (_process is { HasExited: false })
            {
                var line = await _process.StandardError.ReadLineAsync();
                if (line is null)
                {
                    break;
                }

                lock (_stderr)
                {
                    if (_stderr.Length < 16000)
                    {
                        _stderr.AppendLine(line);
                    }
                }
            }
        });
    }

    private async Task<JsonElement> SendRequestCoreAsync(string method, object? parameters, CancellationToken cancellationToken)
    {
        var process = _process ?? throw new AppServerException("codex app-server is not running.");
        var id = _nextRequestId++;
        var payload = JsonSerializer.Serialize(
            new
            {
                id,
                method,
                @params = parameters
            },
            JsonOptions);

        await process.StandardInput.WriteLineAsync(payload);
        await process.StandardInput.FlushAsync(cancellationToken);

        while (true)
        {
            var line = await ReadLineWithTimeoutAsync(process, cancellationToken);
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;

            if (!root.TryGetProperty("id", out var responseId) || responseId.GetInt32() != id)
            {
                continue;
            }

            if (root.TryGetProperty("error", out var error))
            {
                throw new AppServerException($"app-server returned error for {method}: {error.GetRawText()}");
            }

            if (root.TryGetProperty("result", out var result))
            {
                return result.Clone();
            }

            using var empty = JsonDocument.Parse("{}");
            return empty.RootElement.Clone();
        }
    }

    private async Task SendNotificationAsync(string method, CancellationToken cancellationToken)
    {
        var process = _process ?? throw new AppServerException("codex app-server is not running.");
        var payload = JsonSerializer.Serialize(new { method }, JsonOptions);
        await process.StandardInput.WriteLineAsync(payload);
        await process.StandardInput.FlushAsync(cancellationToken);
    }

    private async Task<string?> ReadLineWithTimeoutAsync(Process process, CancellationToken cancellationToken)
    {
        var readTask = process.StandardOutput.ReadLineAsync();
        var timeoutTask = Task.Delay(_requestTimeout, cancellationToken);
        var completed = await Task.WhenAny(readTask, timeoutTask);
        if (completed == timeoutTask)
        {
            throw new AppServerException($"Timed out waiting for codex app-server. {GetStderrSuffix()}");
        }

        var line = await readTask;
        if (line is null)
        {
            throw new AppServerException($"codex app-server exited before responding. {GetStderrSuffix()}");
        }

        return line;
    }

    private string GetStderrSuffix()
    {
        lock (_stderr)
        {
            if (_stderr.Length == 0)
            {
                return string.Empty;
            }

            return $"stderr: {_stderr}";
        }
    }

    private static DateTimeOffset? TryGetUnixTime(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetInt64(out var seconds) => DateTimeOffset.FromUnixTimeSeconds(seconds),
            JsonValueKind.String when long.TryParse(value.GetString(), out var seconds) => DateTimeOffset.FromUnixTimeSeconds(seconds),
            _ => null
        };
    }

    private static bool TryGetString(JsonElement element, string propertyName, out string value)
    {
        value = string.Empty;
        if (!element.TryGetProperty(propertyName, out var property) || property.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return false;
        }

        value = property.ValueKind == JsonValueKind.String ? property.GetString() ?? string.Empty : property.ToString();
        return true;
    }

    private static bool IsChatCwd(string? cwd)
    {
        if (string.IsNullOrWhiteSpace(cwd))
        {
            return false;
        }

        var segments = cwd
            .Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        for (var index = 0; index < segments.Length - 1; index++)
        {
            if (segments[index].Equals("Codex", StringComparison.OrdinalIgnoreCase) &&
                IsDateFolder(segments[index + 1]))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsDateFolder(string value)
    {
        return value.Length == 10 &&
            char.IsDigit(value[0]) &&
            char.IsDigit(value[1]) &&
            char.IsDigit(value[2]) &&
            char.IsDigit(value[3]) &&
            value[4] == '-' &&
            char.IsDigit(value[5]) &&
            char.IsDigit(value[6]) &&
            value[7] == '-' &&
            char.IsDigit(value[8]) &&
            char.IsDigit(value[9]);
    }

    private static (string FileName, string Arguments) ResolveCodexCommand()
    {
        if (!OperatingSystem.IsWindows())
        {
            return ("codex", "app-server --listen stdio://");
        }

        foreach (var candidate in EnumeratePathCandidates())
        {
            var extension = Path.GetExtension(candidate);
            if (extension.Equals(".ps1", StringComparison.OrdinalIgnoreCase))
            {
                return ("powershell.exe", $"-NoLogo -NoProfile -ExecutionPolicy Bypass -File \"{candidate}\" app-server --listen stdio://");
            }

            if (extension.Equals(".cmd", StringComparison.OrdinalIgnoreCase) ||
                extension.Equals(".bat", StringComparison.OrdinalIgnoreCase))
            {
                return ("cmd.exe", $"/d /c \"\"{candidate}\" app-server --listen stdio://\"");
            }

            return (candidate, "app-server --listen stdio://");
        }

        return ("cmd.exe", "/d /c \"codex app-server --listen stdio://\"");
    }

    private static IEnumerable<string> EnumeratePathCandidates()
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        var names = new[] { "codex.cmd", "codex.exe", "codex.bat", "codex.ps1" };
        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            foreach (var name in names)
            {
                var candidate = Path.Combine(directory, name);
                if (File.Exists(candidate))
                {
                    yield return candidate;
                }
            }
        }
    }
}
