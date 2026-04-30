using System.IO;

namespace CodexHelper.Services;

public static class DiagnosticLogService
{
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

    private static void Write(string level, string message, Exception? exception)
    {
        try
        {
            Directory.CreateDirectory(LogDirectory);
            var line = $"{DateTimeOffset.Now:O} [{level}] {message}";
            if (exception is not null)
            {
                line += $"{Environment.NewLine}{exception}";
            }

            lock (Gate)
            {
                File.AppendAllText(LogPath, line + Environment.NewLine);
            }
        }
        catch
        {
        }
    }
}
