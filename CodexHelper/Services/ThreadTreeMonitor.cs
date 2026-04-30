using System.IO;

namespace CodexHelper.Services;

public sealed class ThreadTreeMonitor : IDisposable
{
    private static readonly TimeSpan DebounceInterval = TimeSpan.FromMilliseconds(1200);
    private readonly object _gate = new();
    private readonly List<FileSystemWatcher> _watchers = new();
    private readonly Timer _debounceTimer;
    private bool _disposed;
    private bool _rebuildWatchersOnTick;

    public ThreadTreeMonitor()
    {
        _debounceTimer = new Timer(OnDebounceElapsed);
    }

    public event EventHandler? Changed;

    public void Start()
    {
        RebuildWatchers();
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _debounceTimer.Dispose();
            ClearWatchers();
        }
    }

    private void RebuildWatchers()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            ClearWatchers();

            var codexHome = GetCodexHome();
            if (Directory.Exists(codexHome))
            {
                AddWatcher(codexHome, includeSubdirectories: false, filter: "*", isRootWatcher: true);
            }

            foreach (var root in GetSessionRoots())
            {
                if (Directory.Exists(root))
                {
                    AddWatcher(root, includeSubdirectories: true, filter: "*.jsonl", isRootWatcher: false);
                }
            }
        }
    }

    private void AddWatcher(string path, bool includeSubdirectories, string filter, bool isRootWatcher)
    {
        var watcher = new FileSystemWatcher(path, filter)
        {
            IncludeSubdirectories = includeSubdirectories,
            NotifyFilter = NotifyFilters.FileName |
                NotifyFilters.DirectoryName |
                NotifyFilters.LastWrite |
                NotifyFilters.CreationTime |
                NotifyFilters.Size,
            EnableRaisingEvents = true
        };

        if (isRootWatcher)
        {
            watcher.Created += OnRootChanged;
            watcher.Deleted += OnRootChanged;
            watcher.Renamed += OnRootRenamed;
        }
        else
        {
            watcher.Created += OnSessionChanged;
            watcher.Changed += OnSessionChanged;
            watcher.Deleted += OnSessionChanged;
            watcher.Renamed += OnSessionRenamed;
        }

        watcher.Error += OnWatcherError;
        _watchers.Add(watcher);
    }

    private void ClearWatchers()
    {
        foreach (var watcher in _watchers)
        {
            watcher.Created -= OnRootChanged;
            watcher.Deleted -= OnRootChanged;
            watcher.Renamed -= OnRootRenamed;
            watcher.Created -= OnSessionChanged;
            watcher.Changed -= OnSessionChanged;
            watcher.Deleted -= OnSessionChanged;
            watcher.Renamed -= OnSessionRenamed;
            watcher.Error -= OnWatcherError;
            watcher.Dispose();
        }

        _watchers.Clear();
    }

    private void OnRootChanged(object sender, FileSystemEventArgs e)
    {
        if (IsSessionRootPath(e.FullPath))
        {
            ScheduleChanged(rebuildWatchers: true);
        }
    }

    private void OnRootRenamed(object sender, RenamedEventArgs e)
    {
        if (IsSessionRootPath(e.FullPath) || IsSessionRootPath(e.OldFullPath))
        {
            ScheduleChanged(rebuildWatchers: true);
        }
    }

    private void OnSessionChanged(object sender, FileSystemEventArgs e)
    {
        if (IsRolloutFile(e.FullPath))
        {
            ScheduleChanged(rebuildWatchers: false);
        }
    }

    private void OnSessionRenamed(object sender, RenamedEventArgs e)
    {
        if (IsRolloutFile(e.FullPath) || IsRolloutFile(e.OldFullPath))
        {
            ScheduleChanged(rebuildWatchers: false);
        }
    }

    private void OnWatcherError(object sender, ErrorEventArgs e)
    {
        ScheduleChanged(rebuildWatchers: true);
    }

    private void ScheduleChanged(bool rebuildWatchers)
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _rebuildWatchersOnTick |= rebuildWatchers;
            _debounceTimer.Change(DebounceInterval, Timeout.InfiniteTimeSpan);
        }
    }

    private void OnDebounceElapsed(object? state)
    {
        bool rebuildWatchers;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            rebuildWatchers = _rebuildWatchersOnTick;
            _rebuildWatchersOnTick = false;
        }

        if (rebuildWatchers)
        {
            RebuildWatchers();
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private static string GetCodexHome()
    {
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");
    }

    private static IEnumerable<string> GetSessionRoots()
    {
        var codexHome = GetCodexHome();
        yield return Path.Combine(codexHome, "sessions");
        yield return Path.Combine(codexHome, "archived_sessions");
    }

    private static bool IsSessionRootPath(string path)
    {
        var name = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        return name.Equals("sessions", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("archived_sessions", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsRolloutFile(string path)
    {
        var fileName = Path.GetFileName(path);
        return fileName.StartsWith("rollout-", StringComparison.OrdinalIgnoreCase) &&
            fileName.EndsWith(".jsonl", StringComparison.OrdinalIgnoreCase);
    }
}
