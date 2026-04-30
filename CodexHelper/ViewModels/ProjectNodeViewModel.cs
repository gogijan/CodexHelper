using System.Collections.ObjectModel;
using System.ComponentModel;
using CodexHelper.Infrastructure;
using CodexHelper.Services;

namespace CodexHelper.ViewModels;

public sealed class ProjectNodeViewModel : ObservableObject, IDisposable
{
    private readonly LocalizationService _localization;
    private bool _isUpdatingCheckState;
    private bool _isExpanded;
    private bool _isTreeSelected;
    private bool _disposed;

    public ProjectNodeViewModel(
        string key,
        string displayName,
        string? fullPath,
        IEnumerable<ThreadItemViewModel> threads,
        LocalizationService localization)
    {
        Key = key;
        DisplayName = displayName;
        FullPath = fullPath;
        _localization = localization;

        foreach (var thread in threads)
        {
            var node = new ThreadNodeViewModel(thread, Key);
            node.PropertyChanged += OnThreadNodePropertyChanged;
            Threads.Add(node);
        }
    }

    public string Key { get; }

    public string DisplayName { get; private set; }

    public string? FullPath { get; }

    public bool HasFullPath => !string.IsNullOrWhiteSpace(FullPath);

    public ObservableCollection<ThreadNodeViewModel> Threads { get; } = new();

    public string Header
    {
        get
        {
            var threadCount = _localization.FormatNumber(Threads.Count);
            DateTimeOffset? latestUpdatedAt = null;
            foreach (var thread in Threads)
            {
                if (thread.Thread.Model.UpdatedAt is { } updatedAt &&
                    (latestUpdatedAt is null || updatedAt > latestUpdatedAt.Value))
                {
                    latestUpdatedAt = updatedAt;
                }
            }

            return latestUpdatedAt is null
                ? $"{DisplayName}: {threadCount}"
                : $"{DisplayName}: {threadCount} ({_localization.FormatElapsedSince(latestUpdatedAt.Value, DateTimeOffset.Now)})";
        }
    }

    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetProperty(ref _isExpanded, value);
    }

    public bool IsTreeSelected
    {
        get => _isTreeSelected;
        set => SetProperty(ref _isTreeSelected, value);
    }

    public bool? IsChecked
    {
        get
        {
            if (Threads.Count == 0)
            {
                return false;
            }

            var selected = Threads.Count(thread => thread.IsChecked);
            if (selected == 0)
            {
                return false;
            }

            return selected == Threads.Count ? true : null;
        }
        set
        {
            if (_isUpdatingCheckState)
            {
                return;
            }

            var newValue = value ?? IsChecked != true;

            try
            {
                _isUpdatingCheckState = true;
                foreach (var thread in Threads)
                {
                    thread.IsChecked = newValue;
                }
            }
            finally
            {
                _isUpdatingCheckState = false;
                OnPropertyChanged();
            }
        }
    }

    public void UpdateDisplayName(string displayName)
    {
        DisplayName = displayName;
        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(Header));
    }

    private void OnThreadNodePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ThreadNodeViewModel.IsChecked))
        {
            OnPropertyChanged(nameof(IsChecked));
        }

        if (e.PropertyName == nameof(ThreadNodeViewModel.UpdatedAtText))
        {
            OnPropertyChanged(nameof(Header));
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (var thread in Threads)
        {
            thread.PropertyChanged -= OnThreadNodePropertyChanged;
            thread.Dispose();
        }

        Threads.Clear();
    }
}
