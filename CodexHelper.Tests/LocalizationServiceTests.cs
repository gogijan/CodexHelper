using CodexHelper.Services;

namespace CodexHelper.Tests;

[TestClass]
public sealed class LocalizationServiceTests
{
    [TestMethod]
    public void Indexer_ReturnsLocalizedStringFallbackAndKeyFallback()
    {
        var service = new LocalizationService();

        Assert.AreEqual("Refresh", service["Refresh"]);
        Assert.AreEqual("MissingKey", service["MissingKey"]);

        service.Language = "ru";
        Assert.AreNotEqual("Refresh", service["Refresh"]);

        service.Language = "unsupported";
        Assert.AreEqual("en", service.Language);
        Assert.AreEqual("Refresh", service["Refresh"]);
    }

    [TestMethod]
    public void Language_RaisesChangeNotificationsOnlyWhenNormalizedValueChanges()
    {
        var service = new LocalizationService();
        var languageChangedCount = 0;
        var propertyChangedCount = 0;
        service.LanguageChanged += (_, _) => languageChangedCount++;
        service.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(LocalizationService.Language))
            {
                propertyChangedCount++;
            }
        };

        service.Language = "ru";
        service.Language = "ru";
        service.Language = "missing";

        Assert.AreEqual(2, languageChangedCount);
        Assert.AreEqual(2, propertyChangedCount);
        Assert.AreEqual("en", service.Language);
    }
}
