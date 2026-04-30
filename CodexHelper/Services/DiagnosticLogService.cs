using System.IO;
using System.Text.RegularExpressions;

namespace CodexHelper.Services;

public static class DiagnosticLogService
{
    private const long MaxLogFileBytes = 1024 * 1024;
    private const int RetainedLogFiles = 3;

    private static readonly Regex SecretValuePattern = new(
        "(\"?(?:api[_-]?key|access[_-]?token|refresh[_-]?token|id[_-]?token|token|password|secret)\"?\\s*[:=]\\s*)(\"[^\"]*\"|'[^']*'|[^\\s,;}\\]]+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex BearerPattern = new(
        "\\bBearer\\s+[A-Za-z0-9._~+/=-]+",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex OpenAiKeyPattern = new(
        "\\bsk-[A-Za-z0-9_-]{8,}\\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex GitHubTokenPattern = new(
        "\\b(?:ghp|gho|ghu|ghs|ghr)_[A-Za-z0-9_]{8,}\\b|\\bgithub_pat_[A-Za-z0-9_]{8,}\\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly object Gate = new();

    public static string LogDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CodexHelper",
        "logs");

    public static string LogPath => Path.Combine(LogDirectory, "codexhelper.log");

    public static void Info(string message)
    {
        Write("INFO", message, null);
    }

    public static void Warning(string message, Exception? exception = null)
    {
        Write("WARN", message, exception);
    }

    public static void Error(string message, Exception? exception = null)
    {
        Write("ERROR", message, exception);
    }

    internal static string RedactSensitiveData(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        var redacted = SecretValuePattern.Replace(value, match =>
        {
            var prefix = match.Groups[1].Value;
            var secret = match.Groups[2].Value;
            if (secret.Length >= 2 &&
                ((secret[0] == '"' && secret[^1] == '"') ||
                 (secret[0] == '\'' && secret[^1] == '\'')))
            {
                return $"{prefix}{secret[0]}[redacted]{secret[^1]}";
            }

            return $"{prefix}[redacted]";
        });

        redacted = BearerPattern.Replace(redacted, "Bearer [redacted]");
        redacted = OpenAiKeyPattern.Replace(redacted, "sk-[redacted]");
        return GitHubTokenPattern.Replace(redacted, "[redacted-github-token]");
    }

    private static void Write(string level, string message, Exception? exception)
    {
        try
        {
            var line = $"{DateTimeOffset.Now:O} [{level}] {RedactSensitiveData(message)}";
            if (exception is not null)
            {
                line += $"{Environment.NewLine}{RedactSensitiveData(exception.ToString())}";
            }

            lock (Gate)
            {
                Directory.CreateDirectory(LogDirectory);
                RotateIfNeeded();
                File.AppendAllText(LogPath, line + Environment.NewLine);
            }
        }
        catch
        {
        }
    }

    private static void RotateIfNeeded()
    {
        if (!File.Exists(LogPath))
        {
            return;
        }

        if (new FileInfo(LogPath).Length < MaxLogFileBytes)
        {
            return;
        }

        for (var index = RetainedLogFiles - 1; index >= 1; index--)
        {
            var source = GetRotatedLogPath(index);
            if (File.Exists(source))
            {
                File.Move(source, GetRotatedLogPath(index + 1), overwrite: true);
            }
        }

        File.Move(LogPath, GetRotatedLogPath(1), overwrite: true);
    }

    private static string GetRotatedLogPath(int index)
    {
        return $"{LogPath}.{index}";
    }
}
