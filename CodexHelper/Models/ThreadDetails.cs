namespace CodexHelper.Models;

public sealed class ThreadDetails
{
    public IReadOnlyList<ConversationMessage> Messages { get; init; } = Array.Empty<ConversationMessage>();

    public DateTimeOffset? Timestamp { get; init; }

    public string? Model { get; init; }

    public string? Effort { get; init; }

    public long? ModelContextWindow { get; init; }

    public string DeveloperInstructions { get; init; } = string.Empty;

    public string UserInstructions { get; init; } = string.Empty;

    public string Parameters { get; init; } = string.Empty;
}
