using System.Text.Json;
using CodexHelper.Models;

namespace CodexHelper.Services;

public sealed class CodexThreadService : ICodexThreadService
{
    private readonly CodexAppServerClient _client = new();
    private readonly RolloutThreadReader _rolloutReader = new();

    public bool IsReadOnlyMode { get; private set; }

    public async Task<IReadOnlyList<CodexThread>> GetAllThreadsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var active = await _client.ListThreadsAsync(archived: false, cancellationToken);
            var archived = await _client.ListThreadsAsync(archived: true, cancellationToken);
            IsReadOnlyMode = false;

            return active.Concat(archived)
                .GroupBy(thread => thread.Id, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.OrderBy(thread => thread.IsArchived).First())
                .OrderByDescending(thread => thread.UpdatedAt)
                .ToArray();
        }
        catch (AppServerException ex)
        {
            DiagnosticLogService.Warning("Falling back to read-only rollout index because Codex app-server is unavailable.", ex);
            IsReadOnlyMode = true;
            return await _rolloutReader.GetAllThreadsAsync(cancellationToken);
        }
    }

    public async Task<ThreadDetails> ReadDetailsAsync(CodexThread thread, CancellationToken cancellationToken = default)
    {
        var rolloutDetails = await _rolloutReader.TryReadDetailsAsync(
            thread,
            cancellationToken,
            includeMessages: true);

        if (rolloutDetails.Messages.Count > 0)
        {
            return rolloutDetails;
        }

        if (IsReadOnlyMode)
        {
            return rolloutDetails;
        }

        JsonElement result;
        try
        {
            result = await _client.ReadThreadAsync(thread.Id, cancellationToken);
        }
        catch (AppServerException ex)
        {
            DiagnosticLogService.Warning($"Could not read thread '{thread.Id}' from Codex app-server. Using rollout details only.", ex);
            IsReadOnlyMode = true;
            return rolloutDetails;
        }

        var appServerMessages = ConversationParser.FilterHiddenInstructionMessages(
            ConversationParser.ParseThreadReadResult(result),
            out var appServerDeveloperInstructions,
            out var appServerUserInstructions);

        return new ThreadDetails
        {
            Messages = appServerMessages.Count > 0 ? appServerMessages : rolloutDetails.Messages,
            Timestamp = rolloutDetails.Timestamp,
            Model = rolloutDetails.Model,
            Effort = rolloutDetails.Effort,
            ModelContextWindow = rolloutDetails.ModelContextWindow,
            DeveloperInstructions = FirstNonEmpty(rolloutDetails.DeveloperInstructions, appServerDeveloperInstructions),
            UserInstructions = FirstNonEmpty(rolloutDetails.UserInstructions, appServerUserInstructions),
            Parameters = rolloutDetails.Parameters
        };
    }

    public async Task ElevateAsync(CodexThread thread, CancellationToken cancellationToken = default)
    {
        ThrowIfReadOnly();

        if (!thread.IsArchived)
        {
            await RunWithRetryAsync(
                () => _client.ArchiveThreadAsync(thread.Id, cancellationToken),
                $"archive thread '{thread.Id}'",
                cancellationToken);
        }

        await RunWithRetryAsync(
            () => _client.UnarchiveThreadAsync(thread.Id, cancellationToken),
            $"unarchive thread '{thread.Id}'",
            cancellationToken);
    }

    public async Task ArchiveAsync(CodexThread thread, CancellationToken cancellationToken = default)
    {
        ThrowIfReadOnly();

        if (thread.IsArchived)
        {
            return;
        }

        await RunWithRetryAsync(
            () => _client.ArchiveThreadAsync(thread.Id, cancellationToken),
            $"archive thread '{thread.Id}'",
            cancellationToken);
    }

    public void InvalidateCache()
    {
        _rolloutReader.InvalidateCache();
    }

    public ValueTask DisposeAsync()
    {
        return _client.DisposeAsync();
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;
    }

    private void ThrowIfReadOnly()
    {
        if (IsReadOnlyMode)
        {
            throw new CodexReadOnlyModeException();
        }
    }

    private static async Task RunWithRetryAsync(
        Func<Task> operation,
        string operationName,
        CancellationToken cancellationToken)
    {
        const int maxAttempts = 3;
        Exception? lastException = null;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await operation();
                return;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (attempt < maxAttempts)
            {
                lastException = ex;
                DiagnosticLogService.Warning($"Could not {operationName} on attempt {attempt}. Retrying.", ex);
                await Task.Delay(TimeSpan.FromMilliseconds(250 * attempt * attempt), cancellationToken);
            }
            catch (Exception ex)
            {
                lastException = ex;
            }
        }

        throw new AppServerException($"Could not {operationName} after {maxAttempts} attempts.", lastException!);
    }
}
