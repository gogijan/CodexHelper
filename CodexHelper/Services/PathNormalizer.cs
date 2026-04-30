using System.IO;

namespace CodexHelper.Services;

public static class PathNormalizer
{
    public const string UnknownKey = "__unknown__";

    public static string NormalizeKey(string? cwd)
    {
        var normalized = NormalizeDisplayPath(cwd);
        return string.IsNullOrWhiteSpace(normalized)
            ? UnknownKey
            : normalized.ToUpperInvariant();
    }

    public static string NormalizeDisplayPath(string? cwd)
    {
        if (string.IsNullOrWhiteSpace(cwd))
        {
            return string.Empty;
        }

        var value = cwd.Trim();
        if (value.StartsWith(@"\\?\", StringComparison.Ordinal))
        {
            value = value[4..];
        }

        value = value.Replace('/', '\\').TrimEnd('\\');

        try
        {
            value = Path.GetFullPath(value).TrimEnd('\\');
        }
        catch
        {
        }

        return value;
    }

    public static string GetProjectName(string? cwd, string unknownName)
    {
        var normalized = NormalizeDisplayPath(cwd);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return unknownName;
        }

        var trimmed = normalized.TrimEnd('\\');
        var lastSeparator = trimmed.LastIndexOf('\\');
        return lastSeparator >= 0 && lastSeparator < trimmed.Length - 1
            ? trimmed[(lastSeparator + 1)..]
            : trimmed;
    }
}
