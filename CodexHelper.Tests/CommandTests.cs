using CodexHelper.Infrastructure;

namespace CodexHelper.Tests;

[TestClass]
public sealed class CommandTests
{
    [TestMethod]
    public void RelayCommand_ExecutesOnlyWhenCanExecuteAllowsIt()
    {
        var canExecute = false;
        var executeCount = 0;
        var changedCount = 0;
        var command = new RelayCommand(() => executeCount++, () => canExecute);
        command.CanExecuteChanged += (_, _) => changedCount++;

        command.Execute(null);
        Assert.AreEqual(0, executeCount);
        Assert.IsFalse(command.CanExecute(null));

        canExecute = true;
        command.NotifyCanExecuteChanged();
        command.Execute(null);

        Assert.AreEqual(1, executeCount);
        Assert.IsTrue(command.CanExecute(null));
        Assert.AreEqual(1, changedCount);
    }

    [TestMethod]
    public async Task AsyncCommand_DisablesWhileRunningAndIgnoresConcurrentExecute()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var executeCount = 0;
        var changedCount = 0;
        var command = new AsyncCommand(async () =>
        {
            executeCount++;
            started.SetResult();
            await release.Task;
        });
        command.CanExecuteChanged += (_, _) => changedCount++;

        command.Execute(null);
        await started.Task.WaitAsync(TestTimeout.Short);

        Assert.IsFalse(command.CanExecute(null));
        command.Execute(null);
        Assert.AreEqual(1, executeCount);

        release.SetResult();
        await TestWait.UntilAsync(() => command.CanExecute(null));

        Assert.IsTrue(command.CanExecute(null));
        Assert.AreEqual(1, executeCount);
        Assert.AreEqual(2, changedCount);
    }
}
