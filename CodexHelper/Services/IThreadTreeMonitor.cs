namespace CodexHelper.Services;

public interface IThreadTreeMonitor : IDisposable
{
    event EventHandler? Changed;

    void Start();
}
