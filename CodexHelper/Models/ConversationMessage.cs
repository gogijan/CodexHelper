namespace CodexHelper.Models;

public enum ConversationMessageKind
{
    User,
    Assistant,
    System,
    Tool,
    Reasoning,
    Error,
    Unknown
}

public sealed class ConversationMessage
{
    public ConversationMessage(string role, string content, ConversationMessageKind kind, DateTimeOffset? timestamp = null)
    {
        Role = role;
        Content = content;
        Kind = kind;
        Timestamp = timestamp;
    }

    public string Role { get; }

    public string Content { get; }

    public ConversationMessageKind Kind { get; }

    public DateTimeOffset? Timestamp { get; }
}
