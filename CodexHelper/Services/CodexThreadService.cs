using CodexHelper.Models;

namespace CodexHelper.Services;

public sealed class CodexThreadService : IAsyncDisposable
{
    private readonly CodexAppServerClient _client = new();
    private readonly RolloutThreadReader _rolloutReader = new();

    public async Task<IReadOnlyList<CodexThread>> GetAllThreadsAsync(CancellationToken cancellationToken = default)
    {
        var active = await _client.ListThreadsAsync(archived: false, cancellationToken);
        var archived = await _client.ListThreadsAsync(archived: true, cancellationToken);

        return active.Concat(archived)
            .GroupBy(thread => thread.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderBy(thread => thread.IsArchived).First())
            .OrderByDescending(thread => thread.UpdatedAt)
            .ToArray();
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

        var result = await _client.ReadThreadAsync(thread.Id, cancellationToken);
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
        if (!thread.IsArchived)
        {
            await _client.ArchiveThreadAsync(thread.Id, cancellationToken);
        }

        await _client.UnarchiveThreadAsync(thread.Id, cancellationToken);
    }

    public async Task ArchiveAsync(CodexThread thread, CancellationToken cancellationToken = default)
    {
        if (thread.IsArchived)
        {
            return;
        }

        await _client.ArchiveThreadAsync(thread.Id, cancellationToken);
    }

    public ValueTask DisposeAsync()
    {
        return _client.DisposeAsync();
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;
    }
}
