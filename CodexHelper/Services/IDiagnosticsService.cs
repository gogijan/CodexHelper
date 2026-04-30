using CodexHelper.Models;

namespace CodexHelper.Services;

public interface IDiagnosticsService
{
    Task<DiagnosticsSnapshot> CreateSnapshotAsync(
        bool isReadOnlyMode,
        CancellationToken cancellationToken = default);
}
