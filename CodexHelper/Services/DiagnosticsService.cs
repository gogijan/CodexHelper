using System.IO;
using CodexHelper.Models;

namespace CodexHelper.Services;

public sealed class DiagnosticsService : IDiagnosticsService
{
    private readonly IAppSettingsService _settingsService;

    public DiagnosticsService()
        : this(new AppSettingsService())
    {
    }

    public DiagnosticsService(IAppSettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    public async Task<DiagnosticsSnapshot> CreateSnapshotAsync(
        bool isReadOnlyMode,
        CancellationToken cancellationToken = default)
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var codexHome = Path.Combine(userProfile, ".codex");
        var sessionsRoot = Path.Combine(codexHome, "sessions");
        var archivedSessionsRoot = Path.Combine(codexHome, "archived_sessions");
        var candidates = CodexCliLocator.EnumeratePathCandidates().ToArray();

        string? selectedCodexPath = null;
        string? probeStatus = null;
        try
        {
            var command = await new CodexCliLocator().FindAppServerCommandAsync(cancellationToken);
            selectedCodexPath = ResolveSelectedCodexPath(command, candidates);
            probeStatus = "OK";
        }
        catch (Exception ex) when (ex is AppServerException)
        {
            probeStatus = ex.Message;
        }

        return new DiagnosticsSnapshot
        {
            Mode = isReadOnlyMode ? "Read-only" : "App-server",
            CodexHome = codexHome,
            SessionsRoot = sessionsRoot,
            ArchivedSessionsRoot = archivedSessionsRoot,
            LogPath = DiagnosticLogService.LogPath,
            RolloutIndexCachePath = RolloutThreadReader.CachePath,
            SettingsPath = _settingsService.SettingsPath,
            ActiveRolloutFileCount = CountRolloutFiles(sessionsRoot),
            ArchivedRolloutFileCount = CountRolloutFiles(archivedSessionsRoot),
            CodexCandidates = candidates,
            SelectedCodexPath = selectedCodexPath,
            CodexProbeStatus = probeStatus
        };
    }

    private static string? ResolveSelectedCodexPath(CodexCliCommand command, IReadOnlyList<string> candidates)
    {
        if (!command.FileName.Equals("cmd.exe", StringComparison.OrdinalIgnoreCase) &&
            !command.FileName.Equals("powershell.exe", StringComparison.OrdinalIgnoreCase))
        {
            return command.FileName;
        }

        return candidates.FirstOrDefault(candidate =>
            command.Arguments.Contains(candidate, StringComparison.OrdinalIgnoreCase));
    }

    private static int CountRolloutFiles(string root)
    {
        try
        {
            return Directory.Exists(root)
                ? Directory.EnumerateFiles(root, "rollout-*.jsonl", SearchOption.AllDirectories).Count()
                : 0;
        }
        catch (Exception ex)
        {
            DiagnosticLogService.Warning($"Could not count rollout files under '{root}'.", ex);
            return 0;
        }
    }
}
