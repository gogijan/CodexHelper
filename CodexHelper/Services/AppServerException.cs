namespace CodexHelper.Services;

public sealed class AppServerException : Exception
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
