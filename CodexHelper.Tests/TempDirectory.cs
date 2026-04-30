namespace CodexHelper.Tests;

internal sealed class TempDirectory : IDisposable
{
    private TempDirectory(string directoryPath)
    {
        DirectoryPath = directoryPath;
        Directory.CreateDirectory(DirectoryPath);
    }

    public string DirectoryPath { get; }

    public static TempDirectory Create()
    {
        return new TempDirectory(Path.Combine(Path.GetTempPath(), $"CodexHelper.Tests-{Guid.NewGuid():N}"));
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(DirectoryPath, recursive: true);
        }
        catch
        {
        }
    }
}
