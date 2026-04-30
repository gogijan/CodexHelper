using System.Diagnostics;
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

    private readonly Func<CancellationToken, Task<CodexCliCommand>> _commandProvider;
    private readonly Func<CodexCliCommand, ProcessStartInfo> _startInfoFactory;
    private readonly SemaphoreSlim _processLock = new(1, 1);
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly TimeSpan _requestTimeout;
    private readonly StringBuilder _stderr = new();
    private readonly object _pendingGate = new();
    private readonly Dictionary<int, PendingRequest> _pendingRequests = new();

    private Process? _process;
    private CancellationTokenSource? _processCancellation;
    private Task? _stdoutPump;
    private Task? _stderrPump;
    private int _nextRequestId;
    private bool _initialized;
    private bool _disposed;

    public CodexAppServerClient()
        : this(
            cancellationToken => new CodexCliLocator().FindAppServerCommandAsync(cancellationToken),
            command => CodexCliLocator.CreateStartInfo(command, redirectStandardInput: true),
            TimeSpan.FromSeconds(45))
    {
    }

    internal CodexAppServerClient(
        Func<CancellationToken, Task<CodexCliCommand>> commandProvider,
        Func<CodexCliCommand, ProcessStartInfo> startInfoFactory,
        TimeSpan requestTimeout)
    {
        _commandProvider = commandProvider;
        _startInfoFactory = startInfoFactory;
        _requestTimeout = requestTimeout;
    }

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
        _disposed = true;
        await StopProcessAsync(new AppServerException("Codex app-server client was disposed."));
        _processLock.Dispose();
        _writeLock.Dispose();
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
        await EnsureStartedAsync(cancellationToken);
        return await SendRequestCoreAsync(method, parameters, cancellationToken);
    }

    private async Task EnsureStartedAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        if (_initialized && _process is { HasExited: false })
        {
            return;
        }

        await _processLock.WaitAsync(cancellationToken);
        try
        {
            ThrowIfDisposed();
            if (_initialized && _process is { HasExited: false })
            {
                return;
            }

            await StopProcessCoreAsync(new AppServerException("Codex app-server process was restarted."));
            await StartProcessCoreAsync(cancellationToken);

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
        catch
        {
            await StopProcessCoreAsync(new AppServerException("Codex app-server failed during initialization."));
            throw;
        }
        finally
        {
            _processLock.Release();
        }
    }

    private async Task StartProcessCoreAsync(CancellationToken cancellationToken)
    {
        var command = await _commandProvider(cancellationToken);
        var startInfo = _startInfoFactory(command);

        var process = new Process
        {
            StartInfo = startInfo,
            EnableRaisingEvents = true
        };

        try
        {
            if (!process.Start())
            {
                throw new AppServerException("Could not start codex app-server.");
            }
        }
        catch (Exception ex) when (ex is not AppServerException)
        {
            process.Dispose();
            DiagnosticLogService.Error("Could not start Codex app-server.", ex);
            throw new AppServerException("Could not start codex app-server.", ex);
        }

        _process = process;
        _processCancellation = new CancellationTokenSource();
        _initialized = false;
        _stderr.Clear();
        DiagnosticLogService.Info($"Started Codex app-server process {process.Id}.");

        _stdoutPump = Task.Run(() => PumpStandardOutputAsync(process, _processCancellation.Token));
        _stderrPump = Task.Run(() => PumpStandardErrorAsync(process, _processCancellation.Token));
    }

    private async Task<JsonElement> SendRequestCoreAsync(string method, object? parameters, CancellationToken cancellationToken)
    {
        var id = Interlocked.Increment(ref _nextRequestId);
        var payload = JsonSerializer.Serialize(
            new
            {
                id,
                method,
                @params = parameters
            },
            JsonOptions);

        var pending = new PendingRequest(id, method);
        AddPendingRequest(pending);

        using var timeout = new CancellationTokenSource(_requestTimeout);
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        using var registration = linkedCancellation.Token.Register(
            _ => CancelPendingRequest(id, method, cancellationToken.IsCancellationRequested),
            null);

        try
        {
            await WriteLineAsync(payload, cancellationToken);
            return await pending.Task;
        }
        finally
        {
            RemovePendingRequest(id);
        }
    }

    private async Task SendNotificationAsync(string method, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Serialize(new { method }, JsonOptions);
        await WriteLineAsync(payload, cancellationToken);
    }

    private async Task WriteLineAsync(string payload, CancellationToken cancellationToken)
    {
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            var process = _process;
            if (process is null || process.HasExited)
            {
                throw new AppServerException("codex app-server is not running.");
            }

            await process.StandardInput.WriteLineAsync(payload);
            await process.StandardInput.FlushAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is not AppServerException)
        {
            DiagnosticLogService.Warning("Could not write to Codex app-server.", ex);
            _ = StopProcessAsync(new AppServerException("Codex app-server stopped after a write failure.", ex));
            throw new AppServerException("Could not write to codex app-server.", ex);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private async Task PumpStandardOutputAsync(Process process, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var line = await process.StandardOutput.ReadLineAsync(cancellationToken);
                if (line is null)
                {
                    break;
                }

                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                HandleServerLine(line);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            DiagnosticLogService.Warning("Codex app-server stdout reader failed.", ex);
            CompleteAllPending(new AppServerException($"Codex app-server stdout reader failed. {GetStderrSuffix()}", ex));
        }
        finally
        {
            if (!cancellationToken.IsCancellationRequested && ReferenceEquals(_process, process))
            {
                _initialized = false;
                CompleteAllPending(new AppServerException($"Codex app-server exited before responding. {GetStderrSuffix()}"));
            }
        }
    }

    private async Task PumpStandardErrorAsync(Process process, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var line = await process.StandardError.ReadLineAsync(cancellationToken);
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

                DiagnosticLogService.Warning($"Codex app-server stderr: {line}");
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            DiagnosticLogService.Warning("Codex app-server stderr reader failed.", ex);
        }
        finally
        {
            _ = process;
        }
    }

    private void HandleServerLine(string line)
    {
        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            if (!TryGetResponseId(root, out var id))
            {
                return;
            }

            if (!TryRemovePendingRequest(id, out var pending))
            {
                return;
            }

            if (root.TryGetProperty("error", out var error))
            {
                DiagnosticLogService.Warning($"Codex app-server returned error for {pending.Method}: {error.GetRawText()}");
                pending.SetException(new AppServerException($"app-server returned error for {pending.Method}: {error.GetRawText()}"));
                return;
            }

            if (root.TryGetProperty("result", out var result))
            {
                pending.SetResult(result.Clone());
                return;
            }

            using var empty = JsonDocument.Parse("{}");
            pending.SetResult(empty.RootElement.Clone());
        }
        catch (JsonException ex)
        {
            DiagnosticLogService.Warning($"Ignoring invalid Codex app-server response line: {line}", ex);
        }
        catch (Exception ex)
        {
            DiagnosticLogService.Warning("Could not handle Codex app-server response.", ex);
        }
    }

    private async Task StopProcessAsync(Exception pendingException)
    {
        await _processLock.WaitAsync();
        try
        {
            await StopProcessCoreAsync(pendingException);
        }
        finally
        {
            _processLock.Release();
        }
    }

    private async Task StopProcessCoreAsync(Exception pendingException)
    {
        var process = _process;
        var cancellation = _processCancellation;
        var stdoutPump = _stdoutPump;
        var stderrPump = _stderrPump;

        _process = null;
        _processCancellation = null;
        _stdoutPump = null;
        _stderrPump = null;
        _initialized = false;

        cancellation?.Cancel();
        CompleteAllPending(pendingException);

        if (process is null)
        {
            cancellation?.Dispose();
            return;
        }

        try
        {
            if (!process.HasExited)
            {
                try
                {
                    process.StandardInput.Close();
                }
                catch
                {
                }

                if (!process.WaitForExit(3000))
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync();
                }
            }

            await WaitForPumpAsync(stdoutPump);
            await WaitForPumpAsync(stderrPump);
        }
        finally
        {
            process.Dispose();
            cancellation?.Dispose();
        }
    }

    private static async Task WaitForPumpAsync(Task? pump)
    {
        if (pump is null)
        {
            return;
        }

        try
        {
            await pump;
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
        }
    }

    private void AddPendingRequest(PendingRequest pending)
    {
        lock (_pendingGate)
        {
            _pendingRequests[pending.Id] = pending;
        }
    }

    private void RemovePendingRequest(int id)
    {
        lock (_pendingGate)
        {
            _pendingRequests.Remove(id);
        }
    }

    private bool TryRemovePendingRequest(int id, out PendingRequest pending)
    {
        lock (_pendingGate)
        {
            if (_pendingRequests.TryGetValue(id, out pending!))
            {
                _pendingRequests.Remove(id);
                return true;
            }
        }

        pending = null!;
        return false;
    }

    private void CancelPendingRequest(int id, string method, bool callerCanceled)
    {
        if (!TryRemovePendingRequest(id, out var pending))
        {
            return;
        }

        if (callerCanceled)
        {
            pending.SetCanceled();
            return;
        }

        var exception = new AppServerException($"Timed out waiting for Codex app-server response to {method}. {GetStderrSuffix()}");
        _initialized = false;
        pending.SetException(exception);
        _ = StopProcessAsync(exception);
    }

    private void CompleteAllPending(Exception exception)
    {
        PendingRequest[] pending;
        lock (_pendingGate)
        {
            pending = _pendingRequests.Values.ToArray();
            _pendingRequests.Clear();
        }

        foreach (var request in pending)
        {
            request.SetException(exception);
        }
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

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(CodexAppServerClient));
        }
    }

    private static bool TryGetResponseId(JsonElement element, out int id)
    {
        id = 0;
        if (!element.TryGetProperty("id", out var value))
        {
            return false;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Number => value.TryGetInt32(out id),
            JsonValueKind.String => int.TryParse(value.GetString(), out id),
            _ => false
        };
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

    private sealed class PendingRequest
    {
        private readonly TaskCompletionSource<JsonElement> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public PendingRequest(int id, string method)
        {
            Id = id;
            Method = method;
        }

        public int Id { get; }

        public string Method { get; }

        public Task<JsonElement> Task => _completion.Task;

        public void SetResult(JsonElement result)
        {
            _completion.TrySetResult(result);
        }

        public void SetException(Exception exception)
        {
            _completion.TrySetException(exception);
        }

        public void SetCanceled()
        {
            _completion.TrySetCanceled();
        }
    }
}
