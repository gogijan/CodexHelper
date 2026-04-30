namespace CodexHelper.Models;

public sealed class AppSettings
{
    public string Language { get; set; } = "en";

    public double? WindowWidth { get; set; }

    public double? WindowHeight { get; set; }

    public double? ProjectPaneWidth { get; set; }
}
