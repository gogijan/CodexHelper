using CodexHelper.Models;

namespace CodexHelper.Services;

public interface IAppSettingsService
{
    string SettingsPath { get; }

    AppSettings Load();

    Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default);
}
