namespace CodexHelper.Services;

public interface IThreadTreeMonitor : IDisposable
{
    event EventHandler<ThreadTreeChangedEventArgs>? Changed;

    void Start();
}

public sealed class ThreadTreeChangedEventArgs : EventArgs
{
    public ThreadTreeChangedEventArgs(bool requiresFullRefresh, IEnumerable<string>? changedPaths = null)
    {
        RequiresFullRefresh = requiresFullRefresh;
        ChangedPaths = changedPaths?.ToArray() ?? Array.Empty<string>();
    }

    public bool RequiresFullRefresh { get; }

    public IReadOnlyList<string> ChangedPaths { get; }
}
