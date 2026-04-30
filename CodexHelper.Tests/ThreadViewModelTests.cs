using CodexHelper.Models;
using CodexHelper.Services;
using CodexHelper.ViewModels;

namespace CodexHelper.Tests;

[TestClass]
public sealed class ThreadViewModelTests
{
    [TestMethod]
    public void ThreadItemViewModel_FormatsDerivedPropertiesAndUpdatesState()
    {
        var localization = new LocalizationService();
        var thread = new ThreadItemViewModel(
            new CodexThread
            {
                Id = "thread-1",
                Name = "",
                Cwd = @"C:/Work/CodexHelper/",
                UpdatedAt = DateTimeOffset.Parse("2026-04-30T12:34:00Z")
            },
            localization);

        StringAssert.StartsWith(thread.Name, "Untitled");
        Assert.AreEqual(@"C:\Work\CodexHelper", thread.Cwd);
        Assert.AreEqual("CodexHelper", thread.ProjectName);
        Assert.AreEqual("Active", thread.StateText);

        thread.SetArchived(true);
        Assert.IsTrue(thread.IsArchived);
        Assert.AreEqual("Archived", thread.StateText);

        thread.UpdateFrom(new CodexThread
        {
            Id = "thread-1",
            Name = "Renamed",
            Cwd = null,
            IsArchived = false,
            IsChat = true
        });

        Assert.AreEqual("Renamed", thread.Name);
        Assert.AreEqual("Unknown", thread.ProjectName);
        Assert.IsTrue(thread.IsChat);
        Assert.IsFalse(thread.IsArchived);
    }

    [TestMethod]
    public void ProjectNodeViewModel_CheckStateTracksAndPropagatesThreadSelection()
    {
        var localization = new LocalizationService();
        var first = CreateThreadViewModel("first", localization);
        var second = CreateThreadViewModel("second", localization);
        var project = new ProjectNodeViewModel("project", "Project", @"C:\Project", [first, second]);
        var changedNames = new List<string?>();
        project.PropertyChanged += (_, e) => changedNames.Add(e.PropertyName);

        Assert.AreEqual(false, project.IsChecked);

        first.IsSelected = true;
        Assert.IsNull(project.IsChecked);

        project.IsChecked = null;
        Assert.AreEqual(true, project.IsChecked);
        Assert.IsTrue(first.IsSelected);
        Assert.IsTrue(second.IsSelected);

        project.IsChecked = false;
        Assert.AreEqual(false, project.IsChecked);
        Assert.IsFalse(first.IsSelected);
        Assert.IsFalse(second.IsSelected);
        Assert.IsTrue(changedNames.Contains(nameof(ProjectNodeViewModel.IsChecked)));
    }

    [TestMethod]
    public void ThreadNodeViewModel_ForwardsImportantThreadPropertyChanges()
    {
        var thread = CreateThreadViewModel("thread-1", new LocalizationService());
        var node = new ThreadNodeViewModel(thread, "project");
        var changedNames = new List<string?>();
        node.PropertyChanged += (_, e) => changedNames.Add(e.PropertyName);

        thread.IsSelected = true;
        thread.SetArchived(true);
        thread.SetOpenInCodex(true);

        Assert.IsTrue(node.IsChecked);
        Assert.IsTrue(node.IsArchived);
        Assert.IsTrue(node.IsOpenInCodex);
        CollectionAssert.Contains(changedNames, nameof(ThreadNodeViewModel.IsChecked));
        CollectionAssert.Contains(changedNames, nameof(ThreadNodeViewModel.StateText));
        CollectionAssert.Contains(changedNames, nameof(ThreadNodeViewModel.IsArchived));
        CollectionAssert.Contains(changedNames, nameof(ThreadNodeViewModel.IsOpenInCodex));
    }

    private static ThreadItemViewModel CreateThreadViewModel(string id, LocalizationService localization)
    {
        return new ThreadItemViewModel(
            new CodexThread
            {
                Id = id,
                Name = id,
                Cwd = @"C:\Project"
            },
            localization);
    }
}
