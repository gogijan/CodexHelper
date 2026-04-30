using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using CodexHelper.Infrastructure;
using CodexHelper.Models;
using CodexHelper.Services;

namespace CodexHelper.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private const string ProjectTreeItemKeyPrefix = "project:";
    private const string ThreadTreeItemKeyPrefix = "thread:";

    private readonly AppSettingsService _settingsService = new();
    private readonly CodexThreadService _threadService = new();
    private readonly ThreadTreeMonitor _threadTreeMonitor = new();
    private readonly LocalizationService _localization = new();
    private readonly List<ThreadItemViewModel> _threads = new();
    private readonly HashSet<string> _selectedThreadIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly object _threadLoadCancellationLock = new();
    private readonly SynchronizationContext? _uiContext;
    private CancellationTokenSource? _threadLoadCancellation;
    private int _openThreadRequestId;
    private bool _isRefreshingThreads;
    private bool _pendingAutoRefresh;
    private bool _settingsLoaded;
    private bool _isProjectTreeSelectionTrackingSuppressed;
    private int _projectTreeRestoreVersion;
    private string? _currentProjectTreeItemKey;

    private AppSettings _settings = new();
    private string _selectedLanguageCode = "en";
    private string _selectedThreadFilterCode = "active";
    private string _searchText = string.Empty;
    private string _statusText = string.Empty;
    private bool _isBusy;
    private bool _isTreeLoading;
    private bool _isThreadLoading;
    private ThreadItemViewModel? _selectedThread;
    private IReadOnlyList<ConversationMessage> _conversationMessages = Array.Empty<ConversationMessage>();
    private string _threadTimestampText = string.Empty;
    private string _modelEffortText = string.Empty;
    private string _developerInstructions = string.Empty;
    private string _userInstructions = string.Empty;
    private string _threadParameters = string.Empty;

    public MainViewModel()
    {
        _uiContext = SynchronizationContext.Current;
        LanguageOptions =
        [
            new LanguageOption("en", "English"),
            new LanguageOption("ru", "Русский")
        ];
        UpdateThreadFilterOptions();

        RefreshCommand = new AsyncCommand(RefreshAsync, () => !IsBusy);
        ElevateCommand = new AsyncCommand(ElevateSelectedAsync, () => !IsBusy && SelectedCount > 0);
        ArchiveCommand = new AsyncCommand(ArchiveSelectedAsync, () => !IsBusy && SelectedCount > 0);
        ClearSelectionCommand = new RelayCommand(ClearSelection, () => !IsBusy && SelectedCount > 0);
        ClearSearchCommand = new RelayCommand(ClearSearch, () => HasSearchText);
        _threadTreeMonitor.Changed += OnThreadTreeChanged;

        _localization.LanguageChanged += OnLocalizationLanguageChanged;

        StatusText = _localization["Ready"];
    }

    public ObservableCollection<ProjectNodeViewModel> Projects { get; } = new();

    public ObservableCollection<JsonTreeNodeViewModel> ThreadParameterNodes { get; } = new();

    public ObservableCollection<ThreadFilterOption> ThreadFilterOptions { get; } = new();

    public IReadOnlyList<LanguageOption> LanguageOptions { get; }

    public AsyncCommand RefreshCommand { get; }

    public AsyncCommand ElevateCommand { get; }

    public AsyncCommand ArchiveCommand { get; }

    public RelayCommand ClearSelectionCommand { get; }

    public RelayCommand ClearSearchCommand { get; }

    public string SelectedLanguageCode
    {
        get => _selectedLanguageCode;
        set
        {
            if (SetProperty(ref _selectedLanguageCode, value))
            {
                _localization.Language = value;
                _settings.Language = value;
                _ = SaveSettingsAsync();
            }
        }
    }

    public string SelectedThreadFilterCode
    {
        get => _selectedThreadFilterCode;
        set
        {
            var normalized = NormalizeThreadFilterCode(value);
            if (SetProperty(ref _selectedThreadFilterCode, normalized))
            {
                RebuildProjects();
            }
        }
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
            {
                OnPropertyChanged(nameof(HasSearchText));
                ClearSearchCommand.NotifyCanExecuteChanged();
                RebuildProjects();
            }
        }
    }

    public bool HasSearchText => !string.IsNullOrWhiteSpace(SearchText);

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                NotifyCommands();
            }
        }
    }

    public bool IsTreeLoading
    {
        get => _isTreeLoading;
        private set => SetProperty(ref _isTreeLoading, value);
    }

    public bool IsThreadLoading
    {
        get => _isThreadLoading;
        private set => SetProperty(ref _isThreadLoading, value);
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public ThreadItemViewModel? SelectedThread
    {
        get => _selectedThread;
        private set
        {
            if (SetProperty(ref _selectedThread, value))
            {
                OnPropertyChanged(nameof(HasSelectedThread));
            }
        }
    }

    public bool HasSelectedThread => SelectedThread is not null;

    public IReadOnlyList<ConversationMessage> ConversationMessages
    {
        get => _conversationMessages;
        private set => SetProperty(ref _conversationMessages, value);
    }

    public string ThreadTimestampText
    {
        get => _threadTimestampText;
        private set => SetProperty(ref _threadTimestampText, value);
    }

    public string ModelEffortText
    {
        get => _modelEffortText;
        private set => SetProperty(ref _modelEffortText, value);
    }

    public string DeveloperInstructions
    {
        get => _developerInstructions;
        private set => SetProperty(ref _developerInstructions, value);
    }

    public string UserInstructions
    {
        get => _userInstructions;
        private set => SetProperty(ref _userInstructions, value);
    }

    public string ThreadParameters
    {
        get => _threadParameters;
        private set => SetProperty(ref _threadParameters, value);
    }

    public int SelectedCount => _threads.Count(thread => thread.IsSelected);

    public string SelectedSummary => $"{SelectedCount} {_localization["Selected"]}";

    public string WindowTitle => _localization["AppTitle"];

    public string RefreshText => _localization["Refresh"];

    public string ElevateText => _localization["Elevate"];

    public string ArchiveText => _localization["Archive"];

    public string ClearSelectionText => _localization["ClearSelection"];

    public string RefreshToolTipText => _localization["RefreshToolTip"];

    public string ElevateToolTipText => _localization["ElevateToolTip"];

    public string ArchiveToolTipText => _localization["ArchiveToolTip"];

    public string ClearSelectionToolTipText => _localization["ClearSelectionToolTip"];

    public string SelectedSummaryToolTipText => _localization["SelectedSummaryToolTip"];

    public string ThreadFilterToolTipText => _localization["ThreadFilterToolTip"];

    public string LanguageToolTipText => _localization["LanguageToolTip"];

    public string SearchTextLabel => _localization["Search"];

    public string ProjectsText => _localization["Projects"];

    public string DetailsText => _localization["Details"];

    public string LanguageText => _localization["Language"];

    public string NoSessionText => _localization["NoSession"];

    public string NoMessagesText => _localization["NoMessages"];

    public string UpdatedText => _localization["Updated"];

    public string TimestampText => _localization["Timestamp"];

    public string ModelEffortLabelText => _localization["ModelEffort"];

    public string ProjectText => _localization["Project"];

    public string ThreadIdText => _localization["ThreadId"];

    public string StateText => _localization["State"];

    public string DialogTabText => _localization["DialogTab"];

    public string DeveloperInstructionsTabText => _localization["DeveloperInstructionsTab"];

    public string UserInstructionsTabText => _localization["UserInstructionsTab"];

    public string ParametersTabText => _localization["ParametersTab"];

    public string CopyText => _localization["Copy"];

    public string NoInstructionsText => _localization["NoInstructions"];

    public string NoParametersText => _localization["NoParameters"];

    public string LoadingSessionText => _localization["LoadingSession"];

    public double? SavedWindowWidth => _settings.WindowWidth;

    public double? SavedWindowHeight => _settings.WindowHeight;

    public double? SavedProjectPaneWidth => _settings.ProjectPaneWidth;

    public async Task InitializeAsync()
    {
        await EnsureSettingsLoadedAsync();

        await RefreshAsync();
        _threadTreeMonitor.Start();
    }

    public void LoadSettings()
    {
        if (_settingsLoaded)
        {
            return;
        }

        _settings = _settingsService.Load();
        ApplySettings();
    }

    public void UpdateWindowLayout(
        double width,
        double height,
        double projectPaneWidth)
    {
        if (IsFinitePositive(width) && IsFinitePositive(height))
        {
            _settings.WindowWidth = width;
            _settings.WindowHeight = height;
        }

        if (IsFinitePositive(projectPaneWidth))
        {
            _settings.ProjectPaneWidth = projectPaneWidth;
        }
    }

    private async Task EnsureSettingsLoadedAsync()
    {
        if (_settingsLoaded)
        {
            return;
        }

        _settings = await _settingsService.LoadAsync(_lifetime.Token);
        ApplySettings();
    }

    private void ApplySettings()
    {
        _settingsLoaded = true;
        _selectedLanguageCode = string.IsNullOrWhiteSpace(_settings.Language) ? "en" : _settings.Language;
        _localization.Language = _selectedLanguageCode;
        OnPropertyChanged(nameof(SelectedLanguageCode));
    }

    public async Task OpenThreadAsync(ThreadItemViewModel thread)
    {
        var requestId = Interlocked.Increment(ref _openThreadRequestId);
        var loadCancellation = CreateThreadLoadCancellation();
        var previousThread = SelectedThread;
        if (!ReferenceEquals(previousThread, thread))
        {
            previousThread?.SetOpenInCodex(false);
        }

        SelectedThread = thread;
        thread.SetOpenInCodex(false);
        ClearThreadContent();
        IsThreadLoading = true;
        StatusText = _localization["LoadingSession"];

        try
        {
            var details = await Task.Run(
                () => _threadService.ReadDetailsAsync(thread.Model, loadCancellation.Token),
                loadCancellation.Token);

            if (requestId != _openThreadRequestId || !ReferenceEquals(SelectedThread, thread))
            {
                return;
            }

            ApplyThreadDetails(thread, details);
        }
        catch (ThreadFileLockedException)
        {
            if (requestId != _openThreadRequestId || !ReferenceEquals(SelectedThread, thread))
            {
                return;
            }

            thread.SetOpenInCodex(true);
            StatusText = _localization["ThreadOpenInCodex"];
            await WaitForLockedThreadAsync(thread, requestId, loadCancellation.Token);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            if (requestId != _openThreadRequestId || !ReferenceEquals(SelectedThread, thread))
            {
                return;
            }

            ConversationMessages =
            [
                new ConversationMessage("error", string.Format(_localization["OpenFailed"], ex.Message), ConversationMessageKind.Error)
            ];
            DeveloperInstructions = string.Empty;
            UserInstructions = string.Empty;
            ThreadParameters = string.Empty;
            ClearThreadParameterNodes();
            StatusText = string.Format(_localization["OpenFailed"], ex.Message);
        }
        finally
        {
            ReleaseThreadLoadCancellation(loadCancellation);
            if (requestId == _openThreadRequestId && ReferenceEquals(SelectedThread, thread))
            {
                IsThreadLoading = false;
            }

            RunPendingAutoRefreshIfIdle();
        }
    }

    public void SetCurrentProjectTreeItem(object item)
    {
        if (_isProjectTreeSelectionTrackingSuppressed)
        {
            return;
        }

        _currentProjectTreeItemKey = item switch
        {
            ProjectNodeViewModel project => BuildProjectTreeItemKey(project.Key),
            ThreadNodeViewModel thread => BuildThreadTreeItemKey(thread.ProjectKey, thread.Thread.Id),
            _ => _currentProjectTreeItemKey
        };
    }

    public async Task ShutdownAsync()
    {
        _lifetime.Cancel();
        Interlocked.Increment(ref _openThreadRequestId);
        CancelCurrentThreadLoad();
        ClearThreadContent();
        ClearProjects();
        foreach (var thread in _threads)
        {
            thread.PropertyChanged -= OnThreadPropertyChanged;
        }

        _threads.Clear();
        _selectedThreadIds.Clear();
        _threadTreeMonitor.Changed -= OnThreadTreeChanged;
        _localization.LanguageChanged -= OnLocalizationLanguageChanged;
        _threadTreeMonitor.Dispose();
        await SaveSettingsAsync();
        await _threadService.DisposeAsync();
        _lifetime.Dispose();
    }

    public string GetRoleDisplayName(ConversationMessage message)
    {
        return message.Kind switch
        {
            ConversationMessageKind.User => _localization["User"],
            ConversationMessageKind.Assistant => _localization["Assistant"],
            ConversationMessageKind.System => _localization["System"],
            ConversationMessageKind.Tool => _localization["Tool"],
            ConversationMessageKind.Reasoning => _localization["Reasoning"],
            ConversationMessageKind.Error => _localization["Error"],
            _ => _localization["Unknown"]
        };
    }

    private void ApplyThreadDetails(ThreadItemViewModel thread, ThreadDetails details)
    {
        ConversationMessages = details.Messages.Count > 0
            ? details.Messages
            : [new ConversationMessage("system", _localization["NoMessages"], ConversationMessageKind.System)];
        ThreadTimestampText = details.Timestamp?.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss") ?? string.Empty;
        ModelEffortText = FormatModelEffort(details.Model, details.Effort, details.ModelContextWindow);
        DeveloperInstructions = details.DeveloperInstructions;
        UserInstructions = details.UserInstructions;
        ThreadParameters = details.Parameters;
        StatusText = thread.Name;
    }

    private async Task WaitForLockedThreadAsync(
        ThreadItemViewModel thread,
        int requestId,
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            if (requestId != _openThreadRequestId || !ReferenceEquals(SelectedThread, thread))
            {
                return;
            }

            try
            {
                var details = await Task.Run(
                    () => _threadService.ReadDetailsAsync(thread.Model, cancellationToken),
                    cancellationToken);

                if (requestId != _openThreadRequestId || !ReferenceEquals(SelectedThread, thread))
                {
                    return;
                }

                thread.SetOpenInCodex(false);
                ApplyThreadDetails(thread, details);
                return;
            }
            catch (ThreadFileLockedException)
            {
                thread.SetOpenInCodex(true);
                StatusText = _localization["ThreadOpenInCodex"];
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                if (requestId != _openThreadRequestId || !ReferenceEquals(SelectedThread, thread))
                {
                    return;
                }

                ConversationMessages =
                [
                    new ConversationMessage("error", string.Format(_localization["OpenFailed"], ex.Message), ConversationMessageKind.Error)
                ];
                DeveloperInstructions = string.Empty;
                UserInstructions = string.Empty;
                ThreadParameters = string.Empty;
                ClearThreadParameterNodes();
                StatusText = string.Format(_localization["OpenFailed"], ex.Message);
                return;
            }
        }
    }

    private async Task RefreshAsync()
    {
        await RefreshAsync(preserveSelection: true, automatic: false);
    }

    private async Task RefreshAsync(bool preserveSelection, bool automatic)
    {
        if (_isRefreshingThreads || IsBusy || (automatic && IsThreadLoading))
        {
            if (automatic)
            {
                _pendingAutoRefresh = true;
            }

            return;
        }

        _isRefreshingThreads = true;
        IsTreeLoading = true;
        var previousSelectedId = preserveSelection ? SelectedThread?.Id : null;
        if (!preserveSelection)
        {
            _selectedThreadIds.Clear();
        }

        var existingThreads = preserveSelection
            ? _threads.ToDictionary(thread => thread.Id, StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, ThreadItemViewModel>(StringComparer.OrdinalIgnoreCase);

        IsBusy = true;
        if (!automatic)
        {
            StatusText = _localization["Loading"];
        }

        try
        {
            var threads = await _threadService.GetAllThreadsAsync(_lifetime.Token);
            foreach (var oldThread in _threads)
            {
                oldThread.PropertyChanged -= OnThreadPropertyChanged;
            }

            _threads.Clear();
            foreach (var thread in threads)
            {
                if (!existingThreads.TryGetValue(thread.Id, out var viewModel))
                {
                    viewModel = new ThreadItemViewModel(thread, _localization);
                }
                else
                {
                    viewModel.UpdateFrom(thread);
                }

                viewModel.PropertyChanged += OnThreadPropertyChanged;
                viewModel.IsSelected = _selectedThreadIds.Contains(viewModel.Id) && MatchesThreadFilter(viewModel);
                _threads.Add(viewModel);
            }

            RebuildProjects();
            if (preserveSelection && !string.IsNullOrWhiteSpace(previousSelectedId))
            {
                var selectedThread = _threads.FirstOrDefault(
                    thread => thread.Id.Equals(previousSelectedId, StringComparison.OrdinalIgnoreCase) &&
                        MatchesThreadFilter(thread));
                if (selectedThread is not null)
                {
                    SelectedThread = selectedThread;
                }
                else
                {
                    SelectedThread = null;
                    ClearThreadContent();
                }
            }
            else
            {
                SelectedThread = null;
                ClearThreadContent();
            }

            var projectCount = _threads
                .Select(thread => PathNormalizer.NormalizeKey(thread.Model.Cwd))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count();
            if (!automatic)
            {
                StatusText = string.Format(_localization["RefreshDone"], _threads.Count, projectCount);
            }
        }
        catch (Exception ex)
        {
            if (!automatic)
            {
                StatusText = string.Format(_localization["OperationFailed"], ex.Message);
            }
        }
        finally
        {
            IsTreeLoading = false;
            _isRefreshingThreads = false;
            IsBusy = false;
            RefreshSelectionState();
            RunPendingAutoRefreshIfIdle();
        }
    }

    private async Task ElevateSelectedAsync()
    {
        var selected = GetSelectedThreads();
        if (selected.Count == 0)
        {
            StatusText = _localization["NoSelection"];
            return;
        }

        await RunThreadOperationAsync(
            selected,
            async thread =>
            {
                await _threadService.ElevateAsync(thread.Model, _lifetime.Token);
                thread.SetArchived(false);
            },
            _localization["ElevateDone"]);
    }

    private async Task ArchiveSelectedAsync()
    {
        var selected = GetSelectedThreads();
        if (selected.Count == 0)
        {
            StatusText = _localization["NoSelection"];
            return;
        }

        await RunThreadOperationAsync(
            selected,
            async thread =>
            {
                await _threadService.ArchiveAsync(thread.Model, _lifetime.Token);
                thread.SetArchived(true);
            },
            _localization["ArchiveDone"]);
    }

    private async Task RunThreadOperationAsync(
        IReadOnlyList<ThreadItemViewModel> selected,
        Func<ThreadItemViewModel, Task> operation,
        string successFormat)
    {
        IsBusy = true;
        var completed = 0;

        try
        {
            foreach (var thread in selected)
            {
                StatusText = $"{thread.Name}...";
                await operation(thread);
                completed++;
            }

            StatusText = string.Format(successFormat, completed);
        }
        catch (Exception ex)
        {
            StatusText = string.Format(_localization["OperationFailed"], ex.Message);
        }
        finally
        {
            IsBusy = false;
            RebuildProjects();
            RefreshSelectionState();
            RunPendingAutoRefreshIfIdle();
        }
    }

    private void ClearSelection()
    {
        _selectedThreadIds.Clear();
        foreach (var thread in _threads)
        {
            thread.IsSelected = false;
        }
    }

    private void ClearSearch()
    {
        SearchText = string.Empty;
    }

    private IReadOnlyList<ThreadItemViewModel> GetSelectedThreads()
    {
        return _threads.Where(thread => thread.IsSelected).ToArray();
    }

    private void RebuildProjects()
    {
        var restoreVersion = BeginProjectTreeSelectionRestore();
        ClearSelectionOutsideThreadFilter();
        var projectExpansionState = CaptureProjectExpansionState();

        try
        {
            var filtered = _threads
                .Where(MatchesThreadFilter)
                .Where(MatchesSearch)
                .ToArray();

            ClearProjects();

            var projectThreads = filtered
                .Where(thread => !thread.IsChat)
                .OrderByDescending(thread => thread.Model.UpdatedAt)
                .ToArray();
            var chatThreads = filtered
                .Where(thread => thread.IsChat)
                .OrderByDescending(thread => thread.Model.UpdatedAt)
                .ToArray();
            var allProjectThreads = projectThreads
                .OrderBy(thread => thread.Name, StringComparer.CurrentCultureIgnoreCase)
                .ThenByDescending(thread => thread.Model.UpdatedAt)
                .ToArray();
            Projects.Add(CreateProjectNode(
                "__all__",
                _localization["AllProjects"],
                null,
                allProjectThreads,
                projectExpansionState));

            var projectGroups = projectThreads
                .GroupBy(thread => PathNormalizer.NormalizeKey(thread.Model.Cwd), StringComparer.OrdinalIgnoreCase)
                .OrderBy(group => group.Key == PathNormalizer.UnknownKey ? 1 : 0)
                .ThenBy(group => PathNormalizer.GetProjectName(group.First().Model.Cwd, _localization["UnknownProject"]), StringComparer.CurrentCultureIgnoreCase);

            foreach (var group in projectGroups)
            {
                var first = group.First();
                var fullPath = PathNormalizer.NormalizeDisplayPath(first.Model.Cwd);
                Projects.Add(CreateProjectNode(
                    group.Key,
                    PathNormalizer.GetProjectName(first.Model.Cwd, _localization["UnknownProject"]),
                    string.IsNullOrWhiteSpace(fullPath) ? null : fullPath,
                    group,
                    projectExpansionState));
            }

            if (chatThreads.Length > 0)
            {
                Projects.Add(CreateProjectNode(
                    "__chats__",
                    _localization["Chats"],
                    null,
                    chatThreads,
                    projectExpansionState));
            }

            RestoreCurrentProjectTreeSelection();
        }
        finally
        {
            ResumeProjectTreeSelectionTrackingLater(restoreVersion);
        }
    }

    private int BeginProjectTreeSelectionRestore()
    {
        _isProjectTreeSelectionTrackingSuppressed = true;
        return ++_projectTreeRestoreVersion;
    }

    private void ResumeProjectTreeSelectionTrackingLater(int restoreVersion)
    {
        void Resume()
        {
            RestoreCurrentProjectTreeSelection();
            if (restoreVersion == _projectTreeRestoreVersion)
            {
                _isProjectTreeSelectionTrackingSuppressed = false;
            }
        }

        if (_uiContext is not null)
        {
            _uiContext.Post(_ => Resume(), null);
        }
        else
        {
            Resume();
        }
    }

    private void RestoreCurrentProjectTreeSelection()
    {
        foreach (var project in Projects)
        {
            project.IsTreeSelected = IsCurrentProjectTreeItem(project.Key);
            foreach (var thread in project.Threads)
            {
                thread.IsTreeSelected = IsCurrentThreadTreeItem(thread.ProjectKey, thread.Thread.Id);
            }
        }
    }

    private Dictionary<string, bool> CaptureProjectExpansionState()
    {
        return Projects.ToDictionary(
            project => project.Key,
            project => project.IsExpanded,
            StringComparer.OrdinalIgnoreCase);
    }

    private ProjectNodeViewModel CreateProjectNode(
        string key,
        string displayName,
        string? fullPath,
        IEnumerable<ThreadItemViewModel> threads,
        IReadOnlyDictionary<string, bool> projectExpansionState)
    {
        var project = new ProjectNodeViewModel(key, displayName, fullPath, threads)
        {
            IsExpanded = projectExpansionState.TryGetValue(key, out var isExpanded) && isExpanded,
            IsTreeSelected = IsCurrentProjectTreeItem(key)
        };

        foreach (var thread in project.Threads)
        {
            thread.IsTreeSelected = IsCurrentThreadTreeItem(thread.ProjectKey, thread.Thread.Id);
        }

        return project;
    }

    private bool IsCurrentProjectTreeItem(string projectKey)
    {
        return string.Equals(
            _currentProjectTreeItemKey,
            BuildProjectTreeItemKey(projectKey),
            StringComparison.OrdinalIgnoreCase);
    }

    private bool IsCurrentThreadTreeItem(string projectKey, string threadId)
    {
        return string.Equals(
            _currentProjectTreeItemKey,
            BuildThreadTreeItemKey(projectKey, threadId),
            StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildProjectTreeItemKey(string projectKey)
    {
        return $"{ProjectTreeItemKeyPrefix}{projectKey}";
    }

    private static string BuildThreadTreeItemKey(string projectKey, string threadId)
    {
        return $"{ThreadTreeItemKeyPrefix}{projectKey}:{threadId}";
    }

    private bool MatchesThreadFilter(ThreadItemViewModel thread)
    {
        return SelectedThreadFilterCode switch
        {
            "active" => !thread.IsArchived,
            "archived" => thread.IsArchived,
            _ => true
        };
    }

    private void ClearSelectionOutsideThreadFilter()
    {
        foreach (var thread in _threads)
        {
            if (!_selectedThreadIds.Contains(thread.Id) || MatchesThreadFilter(thread))
            {
                continue;
            }

            _selectedThreadIds.Remove(thread.Id);
            if (thread.IsSelected)
            {
                thread.IsSelected = false;
            }
        }
    }

    private void UpdateThreadFilterOptions()
    {
        AddOrUpdateThreadFilterOption("all", _localization["AllThreadsFilter"]);
        AddOrUpdateThreadFilterOption("active", _localization["ActiveThreadsFilter"]);
        AddOrUpdateThreadFilterOption("archived", _localization["ArchivedThreadsFilter"]);
    }

    private void AddOrUpdateThreadFilterOption(string code, string displayName)
    {
        var option = ThreadFilterOptions.FirstOrDefault(item => item.Code.Equals(code, StringComparison.OrdinalIgnoreCase));
        if (option is null)
        {
            ThreadFilterOptions.Add(new ThreadFilterOption(code, displayName));
            return;
        }

        option.UpdateDisplayName(displayName);
    }

    private static string NormalizeThreadFilterCode(string? value)
    {
        return value is "all" or "active" or "archived" ? value : "active";
    }

    private bool MatchesSearch(ThreadItemViewModel thread)
    {
        if (string.IsNullOrWhiteSpace(SearchText))
        {
            return true;
        }

        var search = SearchText.Trim();
        return Contains(thread.Name, search) ||
            Contains(thread.Id, search) ||
            Contains(thread.Cwd, search) ||
            Contains(thread.ProjectName, search);
    }

    private static bool Contains(string? value, string search)
    {
        return value?.Contains(search, StringComparison.CurrentCultureIgnoreCase) == true;
    }

    private async Task SaveSettingsAsync()
    {
        try
        {
            await _settingsService.SaveAsync(_settings, CancellationToken.None);
        }
        catch
        {
        }
    }

    private void OnThreadPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ThreadItemViewModel.IsSelected))
        {
            if (sender is ThreadItemViewModel thread)
            {
                if (thread.IsSelected)
                {
                    _selectedThreadIds.Add(thread.Id);
                }
                else
                {
                    _selectedThreadIds.Remove(thread.Id);
                }
            }

            RefreshSelectionState();
        }
    }

    private void OnLocalizationLanguageChanged(object? sender, EventArgs e)
    {
        RefreshLocalizedProperties();
        UpdateThreadFilterOptions();
        foreach (var thread in _threads)
        {
            thread.RefreshLocalizedText();
        }

        if (ThreadParameterNodes.Count > 0)
        {
            RebuildThreadParameterNodes();
        }

        RebuildProjects();
    }

    private void OnThreadTreeChanged(object? sender, EventArgs e)
    {
        if (_lifetime.IsCancellationRequested)
        {
            return;
        }

        if (_uiContext is not null)
        {
            _uiContext.Post(_ => QueueAutoRefresh(), null);
        }
        else
        {
            QueueAutoRefresh();
        }
    }

    private void QueueAutoRefresh()
    {
        if (_lifetime.IsCancellationRequested)
        {
            return;
        }

        _pendingAutoRefresh = true;
        RunPendingAutoRefreshIfIdle();
    }

    private void RunPendingAutoRefreshIfIdle()
    {
        if (!_pendingAutoRefresh || _isRefreshingThreads || IsBusy || IsThreadLoading || _lifetime.IsCancellationRequested)
        {
            return;
        }

        _pendingAutoRefresh = false;
        _ = RefreshAsync(preserveSelection: true, automatic: true);
    }

    private void RefreshSelectionState()
    {
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(SelectedSummary));
        NotifyCommands();
    }

    private void NotifyCommands()
    {
        RefreshCommand.NotifyCanExecuteChanged();
        ElevateCommand.NotifyCanExecuteChanged();
        ArchiveCommand.NotifyCanExecuteChanged();
        ClearSelectionCommand.NotifyCanExecuteChanged();
    }

    private void ClearThreadContent()
    {
        _conversationMessages = Array.Empty<ConversationMessage>();
        OnPropertyChanged(nameof(ConversationMessages));
        ThreadTimestampText = string.Empty;
        ModelEffortText = string.Empty;
        DeveloperInstructions = string.Empty;
        UserInstructions = string.Empty;
        ThreadParameters = string.Empty;
        ClearThreadParameterNodes();
    }

    public IReadOnlyList<ConversationMessage> TakeConversationMessagesForRender()
    {
        var messages = _conversationMessages;
        _conversationMessages = Array.Empty<ConversationMessage>();
        return messages;
    }

    private void RefreshLocalizedProperties()
    {
        OnPropertyChanged(nameof(WindowTitle));
        OnPropertyChanged(nameof(RefreshText));
        OnPropertyChanged(nameof(ElevateText));
        OnPropertyChanged(nameof(ArchiveText));
        OnPropertyChanged(nameof(ClearSelectionText));
        OnPropertyChanged(nameof(RefreshToolTipText));
        OnPropertyChanged(nameof(ElevateToolTipText));
        OnPropertyChanged(nameof(ArchiveToolTipText));
        OnPropertyChanged(nameof(ClearSelectionToolTipText));
        OnPropertyChanged(nameof(SelectedSummaryToolTipText));
        OnPropertyChanged(nameof(ThreadFilterToolTipText));
        OnPropertyChanged(nameof(LanguageToolTipText));
        OnPropertyChanged(nameof(SearchTextLabel));
        OnPropertyChanged(nameof(ProjectsText));
        OnPropertyChanged(nameof(DetailsText));
        OnPropertyChanged(nameof(LanguageText));
        OnPropertyChanged(nameof(NoSessionText));
        OnPropertyChanged(nameof(NoMessagesText));
        OnPropertyChanged(nameof(UpdatedText));
        OnPropertyChanged(nameof(TimestampText));
        OnPropertyChanged(nameof(ModelEffortLabelText));
        OnPropertyChanged(nameof(ProjectText));
        OnPropertyChanged(nameof(ThreadIdText));
        OnPropertyChanged(nameof(StateText));
        OnPropertyChanged(nameof(DialogTabText));
        OnPropertyChanged(nameof(DeveloperInstructionsTabText));
        OnPropertyChanged(nameof(UserInstructionsTabText));
        OnPropertyChanged(nameof(ParametersTabText));
        OnPropertyChanged(nameof(CopyText));
        OnPropertyChanged(nameof(NoInstructionsText));
        OnPropertyChanged(nameof(NoParametersText));
        OnPropertyChanged(nameof(LoadingSessionText));
        OnPropertyChanged(nameof(SelectedSummary));
    }

    private CancellationTokenSource CreateThreadLoadCancellation()
    {
        lock (_threadLoadCancellationLock)
        {
            _threadLoadCancellation?.Cancel();
            _threadLoadCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
            return _threadLoadCancellation;
        }
    }

    private void ReleaseThreadLoadCancellation(CancellationTokenSource cancellation)
    {
        lock (_threadLoadCancellationLock)
        {
            if (ReferenceEquals(_threadLoadCancellation, cancellation))
            {
                _threadLoadCancellation = null;
            }
        }

        cancellation.Dispose();
    }

    private void CancelCurrentThreadLoad()
    {
        CancellationTokenSource? cancellation;
        lock (_threadLoadCancellationLock)
        {
            cancellation = _threadLoadCancellation;
            _threadLoadCancellation = null;
        }

        cancellation?.Cancel();
    }

    private static string FormatModelEffort(string? model, string? effort, long? modelContextWindow)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(model))
        {
            parts.Add(model);
        }

        if (!string.IsNullOrWhiteSpace(effort))
        {
            parts.Add(effort);
        }

        if (modelContextWindow is > 0)
        {
            parts.Add(modelContextWindow.Value.ToString("N0", CultureInfo.InvariantCulture));
        }

        return string.Join(" ", parts);
    }

    private static bool IsFinite(double value)
    {
        return !double.IsNaN(value) && !double.IsInfinity(value);
    }

    private static bool IsFinitePositive(double value)
    {
        return IsFinite(value) && value > 0;
    }

    public void RebuildThreadParameterNodes()
    {
        ClearThreadParameterNodes();
        foreach (var node in JsonTreeNodeViewModel.FromJson(ThreadParameters, NoParametersText))
        {
            ThreadParameterNodes.Add(node);
        }
    }

    private void ClearThreadParameterNodes()
    {
        foreach (var node in ThreadParameterNodes)
        {
            node.Dispose();
        }

        ThreadParameterNodes.Clear();
    }

    private void ClearProjects()
    {
        foreach (var project in Projects)
        {
            project.Dispose();
        }

        Projects.Clear();
    }
}
