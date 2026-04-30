namespace CodexHelper.Models;

public sealed class DiagnosticsSnapshot
{
    public required string Mode { get; init; }

    public required string CodexHome { get; init; }

    public required string SessionsRoot { get; init; }

    public required string ArchivedSessionsRoot { get; init; }

    public required string LogPath { get; init; }

    public required string RolloutIndexCachePath { get; init; }

    public required string SettingsPath { get; init; }

    public int ActiveRolloutFileCount { get; init; }

    public int ArchivedRolloutFileCount { get; init; }

    public IReadOnlyList<string> CodexCandidates { get; init; } = Array.Empty<string>();

    public string? SelectedCodexPath { get; init; }

    public string? CodexProbeStatus { get; init; }
}
