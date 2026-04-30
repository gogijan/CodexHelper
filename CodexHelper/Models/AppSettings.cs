using System.Text.Json.Serialization;

namespace CodexHelper.Models;

public sealed class AppSettings
{
    public string Language { get; set; } = "en";

    [JsonIgnore]
    public bool IsLanguageConfigured { get; set; }

    public double? WindowWidth { get; set; }

    public double? WindowHeight { get; set; }

    public double? ProjectPaneWidth { get; set; }
}
