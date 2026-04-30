using System.IO;

namespace CodexHelper.Services;

public sealed class ThreadFileLockedException : IOException
{
    public ThreadFileLockedException(string filePath, IOException innerException)
        : base($"Thread rollout file is locked: {filePath}", innerException)
    {
        FilePath = filePath;
    }

    public string FilePath { get; }
}
