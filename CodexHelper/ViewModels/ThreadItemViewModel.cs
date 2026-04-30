using CodexHelper.Infrastructure;
using CodexHelper.Models;
using CodexHelper.Services;

namespace CodexHelper.ViewModels;

public sealed class ThreadItemViewModel : ObservableObject
{
    private readonly LocalizationService _localization;
    private bool _isSelected;
    private bool _isOpenInCodex;

    public ThreadItemViewModel(CodexThread model, LocalizationService localization)
    {
        Model = model;
        _localization = localization;
    }

    public CodexThread Model { get; private set; }

    public string Id => Model.Id;

    public string Name => string.IsNullOrWhiteSpace(Model.Name)
        ? BuildUntitledName()
        : Model.Name;

    public string Cwd => PathNormalizer.NormalizeDisplayPath(Model.Cwd);

    public string ProjectName => PathNormalizer.GetProjectName(Model.Cwd, _localization["UnknownProject"]);

    public bool IsChat => Model.IsChat;

    public string UpdatedAtText => Model.UpdatedAt?.LocalDateTime.ToString("yyyy-MM-dd HH:mm") ?? string.Empty;

    public bool IsArchived => Model.IsArchived;

    public string StateText => Model.IsArchived ? _localization["Archived"] : _localization["Active"];

    public bool IsOpenInCodex
    {
        get => _isOpenInCodex;
        private set => SetProperty(ref _isOpenInCodex, value);
    }

    public string OpenInCodexText => _localization["OpenInCodex"];

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public void SetArchived(bool isArchived)
    {
        Model.IsArchived = isArchived;
        OnPropertyChanged(nameof(IsArchived));
        OnPropertyChanged(nameof(StateText));
    }

    public void SetOpenInCodex(bool isOpenInCodex)
    {
        IsOpenInCodex = isOpenInCodex;
    }

    public void UpdateFrom(CodexThread model)
    {
        Model = model;
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(Cwd));
        OnPropertyChanged(nameof(ProjectName));
        OnPropertyChanged(nameof(IsChat));
        OnPropertyChanged(nameof(UpdatedAtText));
        OnPropertyChanged(nameof(IsArchived));
        OnPropertyChanged(nameof(StateText));
    }

    public void RefreshLocalizedText()
    {
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(ProjectName));
        OnPropertyChanged(nameof(StateText));
        OnPropertyChanged(nameof(OpenInCodexText));
    }

    private string BuildUntitledName()
    {
        var prefix = _localization["UntitledThread"];
        return string.IsNullOrWhiteSpace(UpdatedAtText)
            ? prefix
            : $"{prefix} {UpdatedAtText}";
    }

}
