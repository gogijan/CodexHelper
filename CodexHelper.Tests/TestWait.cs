namespace CodexHelper.Tests;

internal static class TestTimeout
{
    public static readonly TimeSpan Short = TimeSpan.FromSeconds(5);
}

internal static class TestWait
{
    public static async Task UntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TestTimeout.Short);
        while (!condition())
        {
            await Task.Delay(10, timeout.Token);
        }
    }
}
