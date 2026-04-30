namespace CodexHelper.Models;

public sealed class LanguageOption
{
    public LanguageOption(string code, string displayName)
    {
        Code = code;
        DisplayName = displayName;
    }

    public string Code { get; }

    public string DisplayName { get; }
}
