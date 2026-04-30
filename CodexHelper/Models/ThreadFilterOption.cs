using CodexHelper.Infrastructure;

namespace CodexHelper.Models;

public sealed class ThreadFilterOption : ObservableObject
{
    private string _displayName;

    public ThreadFilterOption(string code, string displayName)
    {
        Code = code;
        _displayName = displayName;
    }

    public string Code { get; }

    public string DisplayName
    {
        get => _displayName;
        private set => SetProperty(ref _displayName, value);
    }

    public void UpdateDisplayName(string displayName)
    {
        DisplayName = displayName;
    }
}
