using CodexHelper.Models;

namespace CodexHelper.Services;

public interface ICodexThreadService : IAsyncDisposable
{
    bool IsReadOnlyMode { get; }

    Task<IReadOnlyList<CodexThread>> GetAllThreadsAsync(CancellationToken cancellationToken = default);

    Task<ThreadDetails> ReadDetailsAsync(CodexThread thread, CancellationToken cancellationToken = default);

    Task ElevateAsync(CodexThread thread, CancellationToken cancellationToken = default);

    Task ArchiveAsync(CodexThread thread, CancellationToken cancellationToken = default);

    void InvalidateCache(IReadOnlyList<string>? changedPaths = null);
}
