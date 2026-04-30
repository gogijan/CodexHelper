namespace CodexHelper.Models;

public sealed class CodexThread
{
    public required string Id { get; init; }

    public string Name { get; init; } = string.Empty;

    public string? Cwd { get; init; }

    public string? Path { get; init; }

    public DateTimeOffset? UpdatedAt { get; init; }

    public bool IsArchived { get; set; }

    public bool IsChat { get; init; }
}
