using System.ComponentModel;
using CodexHelper.Infrastructure;

namespace CodexHelper.ViewModels;

public sealed class ThreadNodeViewModel : ObservableObject, IDisposable
{
    private bool _isTreeSelected;
    private bool _isExpanded;
    private bool _disposed;

    public ThreadNodeViewModel(ThreadItemViewModel thread, string projectKey)
    {
        Thread = thread;
        ProjectKey = projectKey;
        Thread.PropertyChanged += OnThreadPropertyChanged;
    }

    public ThreadItemViewModel Thread { get; }

    public string ProjectKey { get; }

    public string Name => Thread.Name;

    public string UpdatedAtText => Thread.UpdatedAtText;

    public string StateText => Thread.StateText;

    public bool IsArchived => Thread.IsArchived;

    public bool IsOpenInCodex => Thread.IsOpenInCodex;

    public string OpenInCodexText => Thread.OpenInCodexText;

    public bool IsChecked
    {
        get => Thread.IsSelected;
        set => Thread.IsSelected = value;
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

    private void OnThreadPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ThreadItemViewModel.IsSelected))
        {
            OnPropertyChanged(nameof(IsChecked));
        }

        if (e.PropertyName is nameof(ThreadItemViewModel.Name))
        {
            OnPropertyChanged(nameof(Name));
        }

        if (e.PropertyName is nameof(ThreadItemViewModel.UpdatedAtText))
        {
            OnPropertyChanged(nameof(UpdatedAtText));
        }

        if (e.PropertyName is nameof(ThreadItemViewModel.StateText) or nameof(ThreadItemViewModel.IsArchived))
        {
            OnPropertyChanged(nameof(StateText));
            OnPropertyChanged(nameof(IsArchived));
        }

        if (e.PropertyName is nameof(ThreadItemViewModel.IsOpenInCodex) or nameof(ThreadItemViewModel.OpenInCodexText))
        {
            OnPropertyChanged(nameof(IsOpenInCodex));
            OnPropertyChanged(nameof(OpenInCodexText));
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Thread.PropertyChanged -= OnThreadPropertyChanged;
    }
}
