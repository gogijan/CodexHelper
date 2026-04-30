namespace CodexHelper.Services;

public class AppServerException : Exception
{
    public AppServerException(string message)
        : base(message)
    {
    }

    public AppServerException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

public sealed class CodexCliMissingException : AppServerException
{
    public CodexCliMissingException()
        : base("Codex CLI was not found on PATH.")
    {
    }
}

public sealed class CodexAppServerUnsupportedException : AppServerException
{
    public CodexAppServerUnsupportedException(string message)
        : base(message)
    {
    }
}

public sealed class CodexReadOnlyModeException : AppServerException
{
    public CodexReadOnlyModeException()
        : base("This operation is unavailable while CodexHelper is running without Codex app-server.")
    {
    }
}
