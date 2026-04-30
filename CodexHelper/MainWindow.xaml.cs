using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using System.Runtime;
using CodexHelper.Models;
using CodexHelper.Rendering;
using CodexHelper.Services;
using CodexHelper.ViewModels;

namespace CodexHelper;

public partial class MainWindow : Window
{
    private const int ConversationRenderBatchSize = 12;
    private const int ConversationRenderMessageLimit = 500;

    private readonly MainViewModel _viewModel = new();
    private readonly FlowDocument _conversationDocument = ConversationDocumentBuilder.CreateDocument();
    private readonly FlowDocument _developerInstructionsDocument = ConversationDocumentBuilder.CreateDocument();
    private readonly FlowDocument _userInstructionsDocument = ConversationDocumentBuilder.CreateDocument();
    private readonly DispatcherTimer _memoryCleanupTimer;
    private readonly DispatcherTimer _relativeTimeRefreshTimer;
    private CancellationTokenSource? _conversationRenderCancellation;
    private bool _developerInstructionsDirty = true;
    private bool _userInstructionsDirty = true;
    private bool _parametersDirty = true;
    private bool _renderedConversationHasMessages;
    private double? _normalWindowWidth;
    private double? _normalWindowHeight;
    private bool _projectPaneConstraintUpdateQueued;
    private bool _loaded;

    public MainWindow()
    {
        InitializeComponent();
        _viewModel.LoadSettings();
        ApplySavedWindowLayout();
        TrackNormalWindowSize();
        SizeChanged += Window_SizeChanged;
        StateChanged += Window_StateChanged;

        _memoryCleanupTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(4)
        };
        _memoryCleanupTimer.Tick += OnMemoryCleanupTimerTick;

        _relativeTimeRefreshTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMinutes(1)
        };
        _relativeTimeRefreshTimer.Tick += OnRelativeTimeRefreshTimerTick;

        ConversationViewer.Document = _conversationDocument;
        DeveloperInstructionsViewer.Document = _developerInstructionsDocument;
        UserInstructionsViewer.Document = _userInstructionsDocument;
        DataContext = _viewModel;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        _viewModel.DiagnosticsRequested += OnDiagnosticsRequested;
        UpdateAllDocuments();
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        if (_loaded)
        {
            return;
        }

        _loaded = true;
        await _viewModel.InitializeAsync();
        _relativeTimeRefreshTimer.Start();
        UpdateAllDocuments();
    }

    private async void Window_Closed(object? sender, EventArgs e)
    {
        SaveWindowLayout();
        CancelConversationRender();
        _memoryCleanupTimer.Stop();
        _memoryCleanupTimer.Tick -= OnMemoryCleanupTimerTick;
        _relativeTimeRefreshTimer.Stop();
        _relativeTimeRefreshTimer.Tick -= OnRelativeTimeRefreshTimerTick;
        SizeChanged -= Window_SizeChanged;
        StateChanged -= Window_StateChanged;
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _viewModel.DiagnosticsRequested -= OnDiagnosticsRequested;
        ClearViewerDocument(ConversationViewer, _conversationDocument);
        ClearViewerDocument(DeveloperInstructionsViewer, _developerInstructionsDocument);
        ClearViewerDocument(UserInstructionsViewer, _userInstructionsDocument);
        await _viewModel.ShutdownAsync();
        ReleaseUnusedMemory();
    }

    private void ApplySavedWindowLayout()
    {
        EnsureMinimumWindowWidth();

        if (_viewModel.SavedWindowWidth is { } windowWidth &&
            _viewModel.SavedWindowHeight is { } windowHeight &&
            IsUsableLength(windowWidth) &&
            IsUsableLength(windowHeight))
        {
            Width = Math.Clamp(windowWidth, MinWidth, Math.Max(MinWidth, SystemParameters.VirtualScreenWidth));
            Height = Math.Clamp(windowHeight, MinHeight, Math.Max(MinHeight, SystemParameters.VirtualScreenHeight));
        }

        UpdateProjectPaneWidthConstraints();

        if (_viewModel.SavedProjectPaneWidth is { } projectPaneWidth &&
            IsUsableLength(projectPaneWidth))
        {
            ProjectPaneColumn.Width = new GridLength(ClampProjectPaneWidth(projectPaneWidth));
        }
    }

    private void SaveWindowLayout()
    {
        var width = _normalWindowWidth;
        var height = _normalWindowHeight;

        if ((!IsUsableLength(width) || !IsUsableLength(height)) &&
            TryGetNormalWindowBounds(out var bounds))
        {
            width = bounds.Width;
            height = bounds.Height;
        }

        if (width is not { } normalWidth ||
            height is not { } normalHeight ||
            !IsUsableLength(normalWidth) ||
            !IsUsableLength(normalHeight))
        {
            return;
        }

        _viewModel.UpdateWindowLayout(
            normalWidth,
            normalHeight,
            ClampProjectPaneWidth(ProjectPaneColumn.ActualWidth));
    }

    private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        TrackNormalWindowSize();
        UpdateProjectPaneWidthConstraints();
        QueueProjectPaneConstraintUpdate();
    }

    private void Window_StateChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Normal)
        {
            _ = Dispatcher.BeginInvoke(
                DispatcherPriority.ApplicationIdle,
                new Action(TrackNormalWindowSize));
        }
    }

    private void OnDiagnosticsRequested(object? sender, DiagnosticsSnapshot snapshot)
    {
        var text = BuildDiagnosticsText(snapshot);
        var result = MessageBox.Show(
            this,
            text,
            _viewModel.DiagnosticsText,
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Information,
            MessageBoxResult.Cancel);

        if (result == MessageBoxResult.Yes)
        {
            try
            {
                Clipboard.SetText(text);
            }
            catch
            {
            }
        }
        else if (result == MessageBoxResult.No)
        {
            OpenPath(snapshot.LogPath);
        }
    }

    private string BuildDiagnosticsText(DiagnosticsSnapshot snapshot)
    {
        var candidates = snapshot.CodexCandidates.Count == 0
            ? _viewModel.GetLocalizedString("DiagnosticsNone")
            : string.Join(Environment.NewLine, snapshot.CodexCandidates.Select(candidate => $"  - {candidate}"));

        return string.Join(
            Environment.NewLine,
            _viewModel.GetLocalizedString("DiagnosticsTitle"),
            "",
            FormatDiagnosticsLine("DiagnosticsMode", LocalizeDiagnosticsMode(snapshot.Mode)),
            FormatDiagnosticsLine("DiagnosticsCodexProbe", snapshot.CodexProbeStatus ?? _viewModel.GetLocalizedString("DiagnosticsNotChecked")),
            FormatDiagnosticsLine("DiagnosticsSelectedCodex", snapshot.SelectedCodexPath ?? _viewModel.GetLocalizedString("DiagnosticsNone")),
            $"{_viewModel.GetLocalizedString("DiagnosticsCodexCandidates")}:",
            candidates,
            "",
            FormatDiagnosticsLine("DiagnosticsCodexHome", snapshot.CodexHome),
            FormatDiagnosticsLine("DiagnosticsSessions", snapshot.SessionsRoot),
            FormatDiagnosticsLine("DiagnosticsArchivedSessions", snapshot.ArchivedSessionsRoot),
            FormatDiagnosticsLine("DiagnosticsActiveRolloutFiles", snapshot.ActiveRolloutFileCount.ToString("N0", _viewModel.Culture)),
            FormatDiagnosticsLine("DiagnosticsArchivedRolloutFiles", snapshot.ArchivedRolloutFileCount.ToString("N0", _viewModel.Culture)),
            "",
            FormatDiagnosticsLine("DiagnosticsSettings", snapshot.SettingsPath),
            FormatDiagnosticsLine("DiagnosticsRolloutIndexCache", snapshot.RolloutIndexCachePath),
            FormatDiagnosticsLine("DiagnosticsLog", snapshot.LogPath),
            "",
            _viewModel.GetLocalizedString("DiagnosticsCopyAction"),
            _viewModel.GetLocalizedString("DiagnosticsOpenLogAction"),
            _viewModel.GetLocalizedString("DiagnosticsCloseAction"));
    }

    private string FormatDiagnosticsLine(string key, string value)
    {
        return $"{_viewModel.GetLocalizedString(key)}: {value}";
    }

    private string LocalizeDiagnosticsMode(string mode)
    {
        return mode switch
        {
            "Read-only" => _viewModel.GetLocalizedString("DiagnosticsModeReadOnly"),
            "App-server" => _viewModel.GetLocalizedString("DiagnosticsModeAppServer"),
            _ => mode
        };
    }

    private static void OpenPath(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path) ?? string.Empty);
                File.AppendAllText(path, string.Empty);
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true
            });
        }
        catch
        {
        }
    }

    private void TrackNormalWindowSize()
    {
        if (WindowState != WindowState.Normal)
        {
            return;
        }

        if (IsUsableLength(Width) && IsUsableLength(Height))
        {
            _normalWindowWidth = Width;
            _normalWindowHeight = Height;
        }
        else if (IsUsableLength(ActualWidth) && IsUsableLength(ActualHeight))
        {
            _normalWindowWidth = ActualWidth;
            _normalWindowHeight = ActualHeight;
        }
    }

    private bool TryGetNormalWindowBounds(out Rect bounds)
    {
        bounds = WindowState == WindowState.Normal
            ? new Rect(0, 0, Width, Height)
            : RestoreBounds;

        return IsUsableLength(bounds.Width) && IsUsableLength(bounds.Height);
    }

    private void ProjectTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        var shouldRestoreTreeFocus = ProjectTree.IsKeyboardFocusWithin;
        if (e.NewValue is not null && shouldRestoreTreeFocus)
        {
            RestoreProjectTreeItemFocus(e.NewValue);
        }

        if (e.NewValue is not null)
        {
            _viewModel.SetCurrentProjectTreeItem(e.NewValue);
        }

        if (e.NewValue is ThreadNodeViewModel threadNode)
        {
            if (!ReferenceEquals(_viewModel.SelectedThread, threadNode.Thread))
            {
                _ = _viewModel.OpenThreadAsync(threadNode.Thread);
            }
        }
    }

    private void ProjectTree_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Space || Keyboard.Modifiers != ModifierKeys.None)
        {
            return;
        }

        var treeViewItem = FindAncestorTreeViewItem(e.OriginalSource as DependencyObject)
            ?? FindAncestorTreeViewItem(Keyboard.FocusedElement as DependencyObject);
        var item = treeViewItem?.DataContext ?? ProjectTree.SelectedItem;
        if (ToggleProjectTreeCheckState(item))
        {
            e.Handled = true;
            treeViewItem?.Focus();
        }
    }

    private static bool ToggleProjectTreeCheckState(object? item)
    {
        switch (item)
        {
            case ProjectNodeViewModel projectNode:
                projectNode.IsChecked = projectNode.IsChecked != true;
                return true;
            case ThreadNodeViewModel threadNode:
                threadNode.IsChecked = !threadNode.IsChecked;
                return true;
            default:
                return false;
        }
    }

    private void RestoreProjectTreeItemFocus(object selectedItem)
    {
        _ = Dispatcher.BeginInvoke(
            DispatcherPriority.Input,
            new Action(() =>
            {
                var item = FindTreeViewItem(ProjectTree, selectedItem);
                item?.Focus();
            }));
    }

    private static TreeViewItem? FindTreeViewItem(ItemsControl parent, object item)
    {
        if (parent.ItemContainerGenerator.ContainerFromItem(item) is TreeViewItem directItem)
        {
            return directItem;
        }

        foreach (var child in parent.Items)
        {
            if (parent.ItemContainerGenerator.ContainerFromItem(child) is not TreeViewItem childItem)
            {
                continue;
            }

            var nestedItem = FindTreeViewItem(childItem, item);
            if (nestedItem is not null)
            {
                return nestedItem;
            }
        }

        return null;
    }

    private static TreeViewItem? FindAncestorTreeViewItem(DependencyObject? element)
    {
        while (element is not null)
        {
            if (element is TreeViewItem treeViewItem)
            {
                return treeViewItem;
            }

            element = VisualTreeHelper.GetParent(element);
        }

        return null;
    }

    private void ParametersTree_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.C && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            CopySelectedParameterNode();
            e.Handled = true;
        }
    }

    private void CopyParameterMenuItem_Click(object sender, RoutedEventArgs e)
    {
        CopySelectedParameterNode();
    }

    private void ParameterTreeItem_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is TreeViewItem item)
        {
            item.IsSelected = true;
            item.Focus();
        }
    }

    private void RoundedClipHost_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (sender is FrameworkElement element)
        {
            element.Clip = new RectangleGeometry(
                new Rect(0, 0, element.ActualWidth, element.ActualHeight),
                7,
                7);
        }
    }

    private void MainContentGrid_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateProjectPaneWidthConstraints();
        QueueProjectPaneConstraintUpdate();
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.ConversationMessages)
            || (e.PropertyName is nameof(MainViewModel.NoSessionText) && !_viewModel.HasSelectedThread)
            || (e.PropertyName is nameof(MainViewModel.NoMessagesText) && !_renderedConversationHasMessages))
        {
            UpdateConversationDocument();
        }
        else if (e.PropertyName is nameof(MainViewModel.DeveloperInstructions))
        {
            QueueDeveloperInstructionsDocumentUpdate();
        }
        else if (e.PropertyName is nameof(MainViewModel.UserInstructions))
        {
            QueueUserInstructionsDocumentUpdate();
        }
        else if (e.PropertyName is nameof(MainViewModel.NoInstructionsText))
        {
            QueueDeveloperInstructionsDocumentUpdate();
            QueueUserInstructionsDocumentUpdate();
        }
        else if (e.PropertyName is nameof(MainViewModel.ThreadParameters) or nameof(MainViewModel.NoParametersText))
        {
            QueueParametersUpdate();
        }
    }

    private void UpdateAllDocuments()
    {
        UpdateConversationDocument();
        UpdateDeveloperInstructionsDocument();
        UpdateUserInstructionsDocument();
    }

    private void UpdateConversationDocument()
    {
        var messages = _viewModel.ConversationMessages.ToArray();
        _renderedConversationHasMessages = messages.Length > 0;
        StartConversationRender(messages);
    }

    private void QueueDeveloperInstructionsDocumentUpdate()
    {
        _developerInstructionsDirty = true;
        if (DeveloperInstructionsTabButton.IsChecked == true)
        {
            UpdateDeveloperInstructionsDocument();
        }
    }

    private void UpdateDeveloperInstructionsDocument()
    {
        _developerInstructionsDirty = false;
        RebuildViewerDocument(
            DeveloperInstructionsViewer,
            () => ConversationDocumentBuilder.RebuildText(
                _developerInstructionsDocument,
                _viewModel.DeveloperInstructions,
                _viewModel.NoInstructionsText));
    }

    private void QueueUserInstructionsDocumentUpdate()
    {
        _userInstructionsDirty = true;
        if (UserInstructionsTabButton.IsChecked == true)
        {
            UpdateUserInstructionsDocument();
        }
    }

    private void UpdateUserInstructionsDocument()
    {
        _userInstructionsDirty = false;
        RebuildViewerDocument(
            UserInstructionsViewer,
            () => ConversationDocumentBuilder.RebuildText(
                _userInstructionsDocument,
                _viewModel.UserInstructions,
                _viewModel.NoInstructionsText));
    }

    private void QueueParametersUpdate()
    {
        _parametersDirty = true;
        if (ParametersTabButton.IsChecked == true)
        {
            UpdateParametersTree();
        }
    }

    private void UpdateParametersTree()
    {
        _parametersDirty = false;
        _viewModel.RebuildThreadParameterNodes();
        ScheduleMemoryCleanup();
    }

    private void PreviewTab_Checked(object sender, RoutedEventArgs e)
    {
        if (ReferenceEquals(sender, DeveloperInstructionsTabButton) && _developerInstructionsDirty)
        {
            UpdateDeveloperInstructionsDocument();
        }
        else if (ReferenceEquals(sender, UserInstructionsTabButton) && _userInstructionsDirty)
        {
            UpdateUserInstructionsDocument();
        }
        else if (ReferenceEquals(sender, ParametersTabButton) && _parametersDirty)
        {
            UpdateParametersTree();
        }
    }

    private void RebuildViewerDocument(FlowDocumentScrollViewer viewer, Action rebuild)
    {
        ClearViewerSelection(viewer);
        rebuild();
        ScrollToHome(viewer);
        ScheduleMemoryCleanup();
    }

    private void StartConversationRender(IReadOnlyList<ConversationMessage> messages)
    {
        CancelConversationRender();

        var cancellation = new CancellationTokenSource();
        _conversationRenderCancellation = cancellation;
        _ = RenderConversationDocumentAsync(messages, cancellation);
    }

    private async Task RenderConversationDocumentAsync(
        IReadOnlyList<ConversationMessage> messages,
        CancellationTokenSource cancellation)
    {
        var token = cancellation.Token;
        try
        {
            ClearViewerSelection(ConversationViewer);
            ConversationDocumentBuilder.Clear(_conversationDocument);

            if (ConversationDocumentBuilder.TryAddConversationPlaceholder(_conversationDocument, _viewModel, messages))
            {
                ScrollToHome(ConversationViewer);
                ScheduleMemoryCleanup();
                return;
            }

            var messagesToRender = messages;
            if (messages.Count > ConversationRenderMessageLimit)
            {
                var skipped = messages.Count - ConversationRenderMessageLimit;
                messagesToRender = messages.Skip(skipped).ToArray();
                ConversationDocumentBuilder.AddNotice(
                    _conversationDocument,
                    _viewModel.FormatLocalizedString(
                        "ConversationTruncated",
                        messagesToRender.Count,
                        messages.Count));
            }

            ScrollToHome(ConversationViewer);
            var batchStopwatch = Stopwatch.StartNew();
            for (var index = 0; index < messagesToRender.Count; index++)
            {
                token.ThrowIfCancellationRequested();
                var message = messagesToRender[index];
                ConversationDocumentBuilder.AddConversationMessage(
                    _conversationDocument,
                    message,
                    _viewModel.GetRoleDisplayName(message),
                    _viewModel.FormatMessageTimestamp(message.Timestamp));

                if ((index + 1) % ConversationRenderBatchSize == 0 ||
                    batchStopwatch.ElapsedMilliseconds >= 12)
                {
                    await Dispatcher.Yield(DispatcherPriority.Background);
                    batchStopwatch.Restart();
                }
            }

            ScheduleMemoryCleanup();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            ConversationDocumentBuilder.RebuildText(
                _conversationDocument,
                ex.Message,
                _viewModel.NoMessagesText);
        }
        finally
        {
            if (ReferenceEquals(_conversationRenderCancellation, cancellation))
            {
                _conversationRenderCancellation = null;
            }

            cancellation.Dispose();
        }
    }

    private void CancelConversationRender()
    {
        _conversationRenderCancellation?.Cancel();
    }

    private void ClearViewerDocument(FlowDocumentScrollViewer viewer, FlowDocument document)
    {
        ClearViewerSelection(viewer);
        ConversationDocumentBuilder.Clear(document);
        viewer.Document = new FlowDocument();
    }

    private void ScheduleMemoryCleanup()
    {
        _memoryCleanupTimer.Stop();
        _memoryCleanupTimer.Start();
    }

    private void OnMemoryCleanupTimerTick(object? sender, EventArgs e)
    {
        _memoryCleanupTimer.Stop();
        ReleaseUnusedMemory();
    }

    private void OnRelativeTimeRefreshTimerTick(object? sender, EventArgs e)
    {
        _viewModel.RefreshTimeDependentText();
    }

    private static void ReleaseUnusedMemory()
    {
        GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.Default;
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Optimized, blocking: false, compacting: false);
    }

    private static void ClearViewerSelection(FlowDocumentScrollViewer viewer)
    {
        var document = viewer.Document;
        var selection = viewer.Selection;
        if (document is not null && selection is not null)
        {
            selection.Select(document.ContentStart, document.ContentStart);
        }
    }

    private static bool ScrollToHome(DependencyObject root)
    {
        if (root is ScrollViewer scrollViewer)
        {
            scrollViewer.ScrollToHome();
            return true;
        }

        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            if (ScrollToHome(VisualTreeHelper.GetChild(root, index)))
            {
                return true;
            }
        }

        return false;
    }

    private void UpdateProjectPaneWidthConstraints()
    {
        EnsureMinimumWindowWidth();

        var maximumWidth = GetProjectPaneMaximumWidth();
        ProjectPaneColumn.MaxWidth = maximumWidth;
        var currentWidth = GetProjectPanePreferredWidth();
        if (!IsFinite(maximumWidth))
        {
            return;
        }

        var constrainedWidth = Math.Clamp(currentWidth, ProjectPaneColumn.MinWidth, maximumWidth);
        if (ThreadPreviewPane.ActualWidth > 0 &&
            ThreadPreviewPane.ActualWidth < ThreadPreviewPane.MinWidth &&
            ProjectPaneColumn.ActualWidth > ProjectPaneColumn.MinWidth)
        {
            var rightPaneDeficit = ThreadPreviewPane.MinWidth - ThreadPreviewPane.ActualWidth;
            var deficitAdjustedWidth = Math.Max(
                ProjectPaneColumn.MinWidth,
                ProjectPaneColumn.ActualWidth - rightPaneDeficit);
            constrainedWidth = Math.Min(constrainedWidth, deficitAdjustedWidth);
        }

        if (!AreClose(currentWidth, constrainedWidth))
        {
            ProjectPaneColumn.Width = new GridLength(constrainedWidth);
        }
    }

    private double ClampProjectPaneWidth(double width)
    {
        return Math.Clamp(width, ProjectPaneColumn.MinWidth, GetProjectPaneMaximumWidth());
    }

    private double GetProjectPanePreferredWidth()
    {
        if (ProjectPaneColumn.Width.IsAbsolute && IsUsableLength(ProjectPaneColumn.Width.Value))
        {
            return ProjectPaneColumn.Width.Value;
        }

        return ProjectPaneColumn.ActualWidth;
    }

    private double GetProjectPaneMaximumWidth()
    {
        var layoutWidth = GetAvailableContentWidth();

        if (!IsUsableLength(layoutWidth))
        {
            return double.PositiveInfinity;
        }

        var splitterWidth = GetSplitterWidth();
        var maximumByRightPane = layoutWidth - splitterWidth - GetThreadPreviewMinimumColumnWidth();
        return Math.Max(ProjectPaneColumn.MinWidth, maximumByRightPane);
    }

    private void EnsureMinimumWindowWidth()
    {
        ThreadPreviewColumn.MinWidth = GetThreadPreviewMinimumColumnWidth();

        var minimumContentWidth = ProjectPaneColumn.MinWidth + GetSplitterWidth() + ThreadPreviewColumn.MinWidth;
        var contentWidth = GetAvailableContentWidth();
        var chromeWidth = IsUsableLength(ActualWidth) && IsUsableLength(contentWidth)
            ? Math.Max(0, ActualWidth - contentWidth)
            : 0;
        var minimumWindowWidth = minimumContentWidth + chromeWidth;
        MinWidth = minimumWindowWidth;
        if (WindowState == WindowState.Normal && IsUsableLength(Width) && Width < MinWidth)
        {
            Width = MinWidth;
        }
    }

    private void QueueProjectPaneConstraintUpdate()
    {
        if (_projectPaneConstraintUpdateQueued)
        {
            return;
        }

        _projectPaneConstraintUpdateQueued = true;
        _ = Dispatcher.BeginInvoke(
            DispatcherPriority.Render,
            new Action(() =>
            {
                _projectPaneConstraintUpdateQueued = false;
                UpdateProjectPaneWidthConstraints();
            }));
    }

    private double GetAvailableContentWidth()
    {
        if (IsUsableLength(RootGrid.ActualWidth))
        {
            return RootGrid.ActualWidth;
        }

        if (Content is FrameworkElement contentElement && IsUsableLength(contentElement.ActualWidth))
        {
            return contentElement.ActualWidth;
        }

        return IsUsableLength(ActualWidth) ? ActualWidth : Width;
    }

    private double GetThreadPreviewMinimumColumnWidth()
    {
        return ThreadPreviewPane.MinWidth +
            ThreadPreviewPane.Margin.Left +
            ThreadPreviewPane.Margin.Right;
    }

    private double GetSplitterWidth()
    {
        return IsUsableLength(SplitterColumn.ActualWidth)
            ? SplitterColumn.ActualWidth
            : SplitterColumn.Width.Value;
    }

    private static bool IsFinite(double? value)
    {
        return value is { } number && IsFinite(number);
    }

    private static bool IsFinite(double value)
    {
        return !double.IsNaN(value) && !double.IsInfinity(value);
    }

    private static bool IsUsableLength(double? value)
    {
        return value is { } number && IsFinite(number) && number > 0;
    }

    private static bool AreClose(double left, double right)
    {
        return Math.Abs(left - right) < 0.5;
    }

    private void CopySelectedParameterNode()
    {
        if (ParametersTree.SelectedItem is not JsonTreeNodeViewModel node)
        {
            return;
        }

        try
        {
            Clipboard.SetText(node.ToCopyText());
        }
        catch
        {
        }
    }
}
