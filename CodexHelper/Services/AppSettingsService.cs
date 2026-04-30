using System.IO;
using System.Text.Json;
using CodexHelper.Models;

namespace CodexHelper.Services;

public sealed class AppSettingsService : IAppSettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public AppSettingsService(string? settingsDirectory = null)
    {
        SettingsDirectory = settingsDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CodexHelper");
    }

    public string SettingsDirectory { get; }

    public string SettingsPath => Path.Combine(SettingsDirectory, "settings.json");

    public AppSettings Load()
    {
        if (!File.Exists(SettingsPath))
        {
            return new AppSettings();
        }

        try
        {
            using var stream = File.OpenRead(SettingsPath);
            using var document = JsonDocument.Parse(stream);
            return ReadSettings(document);
        }
        catch
        {
            return new AppSettings();
        }
    }

    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(SettingsPath))
        {
            return new AppSettings();
        }

        try
        {
            await using var stream = File.OpenRead(SettingsPath);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            return ReadSettings(document);
        }
        catch
        {
            return new AppSettings();
        }
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(SettingsDirectory);
        var temporaryPath = Path.Combine(
            SettingsDirectory,
            $"{Path.GetFileName(SettingsPath)}.{Guid.NewGuid():N}.tmp");

        try
        {
            await using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 16 * 1024,
                FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, settings, JsonOptions, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (File.Exists(SettingsPath))
            {
                File.Replace(temporaryPath, SettingsPath, destinationBackupFileName: null);
            }
            else
            {
                File.Move(temporaryPath, SettingsPath);
            }
        }
        finally
        {
            TryDeleteFile(temporaryPath);
        }
    }

    private static AppSettings ReadSettings(JsonDocument document)
    {
        var settings = JsonSerializer.Deserialize<AppSettings>(document.RootElement.GetRawText(), JsonOptions)
            ?? new AppSettings();
        settings.IsLanguageConfigured = HasConfiguredLanguage(document.RootElement);
        return settings;
    }

    private static bool HasConfiguredLanguage(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        foreach (var property in root.EnumerateObject())
        {
            if (!property.Name.Equals(nameof(AppSettings.Language), StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return property.Value.ValueKind == JsonValueKind.String &&
                !string.IsNullOrWhiteSpace(property.Value.GetString());
        }

        return false;
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
        }
    }
}
