using CodexHelper.Models;
using CodexHelper.Services;

namespace CodexHelper.Tests;

[TestClass]
public sealed class AppSettingsServiceTests
{
    [TestMethod]
    public void Load_ReturnsDefaultSettingsWhenFileIsMissing()
    {
        using var temp = TempDirectory.Create();
        var service = new AppSettingsService(temp.DirectoryPath);

        var settings = service.Load();

        Assert.AreEqual(Path.Combine(temp.DirectoryPath, "settings.json"), service.SettingsPath);
        AssertDefaultSettings(settings);
    }

    [TestMethod]
    public async Task SaveAsync_AndLoadAsync_RoundTripSettings()
    {
        using var temp = TempDirectory.Create();
        var service = new AppSettingsService(temp.DirectoryPath);
        var expected = new AppSettings
        {
            Language = "ru",
            WindowWidth = 1200,
            WindowHeight = 800,
            ProjectPaneWidth = 360
        };

        await service.SaveAsync(expected);
        var actual = await service.LoadAsync();

        Assert.AreEqual("ru", actual.Language);
        Assert.IsTrue(actual.IsLanguageConfigured);
        Assert.AreEqual(1200, actual.WindowWidth);
        Assert.AreEqual(800, actual.WindowHeight);
        Assert.AreEqual(360, actual.ProjectPaneWidth);
    }

    [TestMethod]
    public async Task LoadAsync_ReturnsDefaultSettingsWhenJsonIsInvalid()
    {
        using var temp = TempDirectory.Create();
        var service = new AppSettingsService(temp.DirectoryPath);
        Directory.CreateDirectory(temp.DirectoryPath);
        await File.WriteAllTextAsync(service.SettingsPath, "{ invalid json");

        var settings = await service.LoadAsync();

        AssertDefaultSettings(settings);
    }

    [TestMethod]
    public async Task LoadAsync_MarksLanguageUnconfiguredWhenLanguagePropertyIsMissing()
    {
        using var temp = TempDirectory.Create();
        var service = new AppSettingsService(temp.DirectoryPath);
        Directory.CreateDirectory(temp.DirectoryPath);
        await File.WriteAllTextAsync(service.SettingsPath, """{"WindowWidth":1200}""");

        var settings = await service.LoadAsync();

        Assert.AreEqual("en", settings.Language);
        Assert.IsFalse(settings.IsLanguageConfigured);
        Assert.AreEqual(1200, settings.WindowWidth);
    }

    [TestMethod]
    public async Task SaveAsync_CreatesSettingsDirectory()
    {
        using var temp = TempDirectory.Create();
        var nestedDirectory = Path.Combine(temp.DirectoryPath, "missing", "settings");
        var service = new AppSettingsService(nestedDirectory);

        await service.SaveAsync(new AppSettings { Language = "en" });

        Assert.IsTrue(File.Exists(service.SettingsPath));
    }

    [TestMethod]
    public async Task SaveAsync_DoesNotLeaveTemporaryFilesAfterSuccessfulSave()
    {
        using var temp = TempDirectory.Create();
        var service = new AppSettingsService(temp.DirectoryPath);

        await service.SaveAsync(new AppSettings { Language = "ru" });

        Assert.AreEqual(0, Directory.EnumerateFiles(temp.DirectoryPath, "*.tmp").Count());
    }

    private static void AssertDefaultSettings(AppSettings settings)
    {
        Assert.AreEqual("en", settings.Language);
        Assert.IsFalse(settings.IsLanguageConfigured);
        Assert.IsNull(settings.WindowWidth);
        Assert.IsNull(settings.WindowHeight);
        Assert.IsNull(settings.ProjectPaneWidth);
    }
}
