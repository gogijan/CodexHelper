using CodexHelper.Models;
using CodexHelper.Services;
using CodexHelper.ViewModels;

namespace CodexHelper.Tests;

[TestClass]
public sealed class MainViewModelTests
{
    [TestMethod]
    public async Task InitializeAsync_LoadsThreadsIntoActiveProjectTreeAndStartsMonitor()
    {
        var harness = CreateHarness();

        await harness.ViewModel.InitializeAsync();

        Assert.IsTrue(harness.Monitor.StartCalled);
        Assert.AreEqual("active", harness.ViewModel.SelectedThreadFilterCode);
        Assert.AreEqual(4, harness.ViewModel.Projects.Count);
        Assert.AreEqual("__all__", harness.ViewModel.Projects[0].Key);
        Assert.AreEqual(2, harness.ViewModel.Projects[0].Threads.Count);
        AssertProjectContainsThreads(harness.ViewModel.Projects[0], "alpha", "unknown");
        AssertProjectContainsThreads(harness.ViewModel.Projects.Single(project => project.DisplayName == "Alpha"), "alpha");
        AssertProjectContainsThreads(harness.ViewModel.Projects.Single(project => project.DisplayName == "Unknown"), "unknown");
        AssertProjectContainsThreads(harness.ViewModel.Projects.Single(project => project.Key == "__chats__"), "chat");

        await harness.ViewModel.ShutdownAsync();
    }

    [TestMethod]
    public async Task ThreadFilterAndSearch_RebuildProjectTree()
    {
        var harness = CreateHarness();
        await harness.ViewModel.InitializeAsync();

        harness.ViewModel.SelectedThreadFilterCode = "all";
        harness.ViewModel.SearchText = "Beta";

        Assert.AreEqual(2, harness.ViewModel.Projects.Count);
        AssertProjectContainsThreads(harness.ViewModel.Projects[0], "beta");
        AssertProjectContainsThreads(harness.ViewModel.Projects[1], "beta");

        harness.ViewModel.SearchText = string.Empty;
        harness.ViewModel.SelectedThreadFilterCode = "invalid";
        Assert.AreEqual("active", harness.ViewModel.SelectedThreadFilterCode);
        Assert.AreEqual(2, harness.ViewModel.Projects[0].Threads.Count);
        Assert.IsFalse(harness.ViewModel.Projects[0].Threads.Any(thread => thread.Thread.Id == "beta"));

        await harness.ViewModel.ShutdownAsync();
    }

    [TestMethod]
    public async Task ChangingFilterClearsSelectionOutsideNewFilter()
    {
        var harness = CreateHarness();
        await harness.ViewModel.InitializeAsync();
        var alphaNode = harness.ViewModel.Projects[0].Threads.Single(thread => thread.Thread.Id == "alpha");

        alphaNode.IsChecked = true;
        Assert.AreEqual(1, harness.ViewModel.SelectedCount);

        harness.ViewModel.SelectedThreadFilterCode = "archived";

        Assert.AreEqual(0, harness.ViewModel.SelectedCount);
        Assert.IsFalse(alphaNode.IsChecked);
        AssertProjectContainsThreads(harness.ViewModel.Projects[0], "beta");

        await harness.ViewModel.ShutdownAsync();
    }

    [TestMethod]
    public async Task OpenThreadAsync_AppliesDetailsAndRebuildsParameterNodes()
    {
        var harness = CreateHarness();
        await harness.ViewModel.InitializeAsync();
        var alpha = harness.ViewModel.Projects[0].Threads.Single(thread => thread.Thread.Id == "alpha").Thread;

        await harness.ViewModel.OpenThreadAsync(alpha);

        Assert.AreSame(alpha, harness.ViewModel.SelectedThread);
        Assert.AreEqual("Alpha thread", harness.ViewModel.StatusText);
        Assert.AreEqual("gpt-5 high 200,000", harness.ViewModel.ModelEffortText);
        Assert.AreEqual("Dev", harness.ViewModel.DeveloperInstructions);
        Assert.AreEqual("User", harness.ViewModel.UserInstructions);
        CollectionAssert.AreEqual(
            new[] { "Hello from alpha" },
            harness.ViewModel.ConversationMessages.Select(message => message.Content).ToArray());

        harness.ViewModel.RebuildThreadParameterNodes();
        Assert.AreEqual(1, harness.ViewModel.ThreadParameterNodes.Count);
        Assert.AreEqual("parameters", harness.ViewModel.ThreadParameterNodes[0].Name);
        Assert.AreEqual("alpha", harness.ViewModel.ThreadParameterNodes[0].Children.Single().Value);

        var messagesForRender = harness.ViewModel.TakeConversationMessagesForRender();
        Assert.AreEqual(1, messagesForRender.Count);
        Assert.AreEqual(0, harness.ViewModel.ConversationMessages.Count);

        await harness.ViewModel.ShutdownAsync();
    }

    [TestMethod]
    public async Task OpenThreadAsync_UsesNoMessagesPlaceholderWhenDetailsHaveNoMessages()
    {
        var harness = CreateHarness();
        await harness.ViewModel.InitializeAsync();
        var unknown = harness.ViewModel.Projects[0].Threads.Single(thread => thread.Thread.Id == "unknown").Thread;

        await harness.ViewModel.OpenThreadAsync(unknown);

        Assert.AreEqual(1, harness.ViewModel.ConversationMessages.Count);
        Assert.AreEqual(ConversationMessageKind.System, harness.ViewModel.ConversationMessages[0].Kind);
        Assert.AreEqual("No displayable messages were returned for this session.", harness.ViewModel.ConversationMessages[0].Content);

        await harness.ViewModel.ShutdownAsync();
    }

    [TestMethod]
    public async Task MonitorRefresh_ReloadsCurrentlyOpenThreadDetails()
    {
        var harness = CreateHarness();
        await harness.ViewModel.InitializeAsync();
        var alpha = harness.ViewModel.Projects[0].Threads.Single(thread => thread.Thread.Id == "alpha").Thread;
        await harness.ViewModel.OpenThreadAsync(alpha);

        harness.ThreadService.DetailsById["alpha"] = new ThreadDetails
        {
            Messages = [new ConversationMessage("assistant", "Updated after refresh", ConversationMessageKind.Assistant)],
            Timestamp = DateTimeOffset.Parse("2026-04-30T12:06:00Z"),
            Model = "gpt-5.4",
            Effort = "medium",
            Parameters = """{"id":"alpha","version":2}"""
        };

        harness.Monitor.RaiseChanged();

        await TestWait.UntilAsync(() =>
            harness.ViewModel.ConversationMessages.Any(message => message.Content == "Updated after refresh"));

        Assert.IsTrue(harness.ThreadService.CacheInvalidated);
        Assert.AreEqual("gpt-5.4 medium", harness.ViewModel.ModelEffortText);
        Assert.AreSame(alpha, harness.ViewModel.SelectedThread);

        await harness.ViewModel.ShutdownAsync();
    }

    [TestMethod]
    public async Task MonitorRefresh_ForwardsChangedPathsToThreadService()
    {
        var harness = CreateHarness();
        await harness.ViewModel.InitializeAsync();
        var changedPath = @"C:\Users\tester\.codex\sessions\2026\05\01\rollout-thread-1.jsonl";

        harness.Monitor.RaiseChanged(changedPath);

        await TestWait.UntilAsync(() => harness.ThreadService.InvalidatedPaths.Count > 0);

        CollectionAssert.AreEqual(new[] { changedPath }, harness.ThreadService.InvalidatedPaths.ToArray());

        await harness.ViewModel.ShutdownAsync();
    }

    private static MainViewModelHarness CreateHarness()
    {
        var threadService = new FakeCodexThreadService
        {
            Threads =
            [
                CreateThread("alpha", "Alpha thread", @"C:\Work\Alpha", "2026-04-30T12:00:00Z"),
                CreateThread("beta", "Beta archived", @"C:\Work\Beta", "2026-04-30T13:00:00Z", isArchived: true),
                CreateThread("chat", "Chat thread", @"C:\Users\tester\Codex\2026-04-30", "2026-04-30T14:00:00Z", isChat: true),
                CreateThread("unknown", "Unknown thread", null, "2026-04-30T11:00:00Z")
            ]
        };
        threadService.DetailsById["alpha"] = new ThreadDetails
        {
            Messages = [new ConversationMessage("user", "Hello from alpha", ConversationMessageKind.User)],
            Timestamp = DateTimeOffset.Parse("2026-04-30T12:05:00Z"),
            Model = "gpt-5",
            Effort = "high",
            ModelContextWindow = 200000,
            DeveloperInstructions = "Dev",
            UserInstructions = "User",
            Parameters = """{"id":"alpha"}"""
        };
        threadService.DetailsById["unknown"] = new ThreadDetails
        {
            Parameters = """{"id":"unknown"}"""
        };

        var settingsService = new FakeAppSettingsService();
        var monitor = new FakeThreadTreeMonitor();
        var diagnostics = new FakeDiagnosticsService();
        var viewModel = new MainViewModel(
            settingsService,
            threadService,
            monitor,
            diagnostics,
            new LocalizationService());

        return new MainViewModelHarness(viewModel, threadService, settingsService, monitor, diagnostics);
    }

    private static CodexThread CreateThread(
        string id,
        string name,
        string? cwd,
        string updatedAt,
        bool isArchived = false,
        bool isChat = false)
    {
        return new CodexThread
        {
            Id = id,
            Name = name,
            Cwd = cwd,
            UpdatedAt = DateTimeOffset.Parse(updatedAt),
            IsArchived = isArchived,
            IsChat = isChat
        };
    }

    private static void AssertProjectContainsThreads(ProjectNodeViewModel project, params string[] ids)
    {
        CollectionAssert.AreEqual(
            ids,
            project.Threads.Select(thread => thread.Thread.Id).ToArray());
    }

    private sealed record MainViewModelHarness(
        MainViewModel ViewModel,
        FakeCodexThreadService ThreadService,
        FakeAppSettingsService SettingsService,
        FakeThreadTreeMonitor Monitor,
        FakeDiagnosticsService Diagnostics);

    private sealed class FakeAppSettingsService : IAppSettingsService
    {
        public AppSettings Settings { get; set; } = new();

        public string SettingsPath { get; } = @"C:\Temp\settings.json";

        public AppSettings Load()
        {
            return Settings;
        }

        public Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Settings);
        }

        public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
        {
            Settings = settings;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeCodexThreadService : ICodexThreadService
    {
        public bool IsReadOnlyMode { get; set; }

        public IReadOnlyList<CodexThread> Threads { get; set; } = Array.Empty<CodexThread>();

        public Dictionary<string, ThreadDetails> DetailsById { get; } = new(StringComparer.OrdinalIgnoreCase);

        public bool CacheInvalidated { get; private set; }

        public List<string> InvalidatedPaths { get; } = [];

        public Task<IReadOnlyList<CodexThread>> GetAllThreadsAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Threads);
        }

        public Task<ThreadDetails> ReadDetailsAsync(CodexThread thread, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(DetailsById.TryGetValue(thread.Id, out var details) ? details : new ThreadDetails());
        }

        public Task ElevateAsync(CodexThread thread, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task ArchiveAsync(CodexThread thread, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public void InvalidateCache(IReadOnlyList<string>? changedPaths = null)
        {
            CacheInvalidated = true;
            InvalidatedPaths.Clear();
            if (changedPaths is not null)
            {
                InvalidatedPaths.AddRange(changedPaths);
            }
        }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FakeThreadTreeMonitor : IThreadTreeMonitor
    {
        public event EventHandler<ThreadTreeChangedEventArgs>? Changed;

        public bool StartCalled { get; private set; }

        public void Start()
        {
            StartCalled = true;
        }

        public void RaiseChanged(params string[] changedPaths)
        {
            Changed?.Invoke(this, new ThreadTreeChangedEventArgs(requiresFullRefresh: false, changedPaths));
        }

        public void Dispose()
        {
        }
    }

    private sealed class FakeDiagnosticsService : IDiagnosticsService
    {
        public Task<DiagnosticsSnapshot> CreateSnapshotAsync(
            bool isReadOnlyMode,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new DiagnosticsSnapshot
            {
                Mode = isReadOnlyMode ? "Read-only" : "App-server",
                CodexHome = @"C:\Users\tester\.codex",
                SessionsRoot = @"C:\Users\tester\.codex\sessions",
                ArchivedSessionsRoot = @"C:\Users\tester\.codex\archived_sessions",
                LogPath = @"C:\Temp\codexhelper.log",
                RolloutIndexCachePath = @"C:\Temp\rollout-index.json",
                SettingsPath = @"C:\Temp\settings.json"
            });
        }
    }
}
