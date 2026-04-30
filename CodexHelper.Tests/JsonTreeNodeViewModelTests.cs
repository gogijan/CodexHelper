using System.Text.Json;
using CodexHelper.ViewModels;

namespace CodexHelper.Tests;

[TestClass]
public sealed class JsonTreeNodeViewModelTests
{
    [TestMethod]
    public void FromJson_ReturnsEmptyNodeForBlankJson()
    {
        var nodes = JsonTreeNodeViewModel.FromJson("   ", "No parameters");

        Assert.AreEqual(1, nodes.Count);
        Assert.AreEqual("No parameters", nodes[0].Name);
        Assert.AreEqual("empty", nodes[0].Type);
    }

    [TestMethod]
    public void FromJson_BuildsTreeAndCopyTextFromObject()
    {
        const string json = """
            {
              "name": "Alpha",
              "items": [1, true, null, { "nested": "value" }]
            }
            """;

        var root = JsonTreeNodeViewModel.FromJson(json, "empty").Single();

        Assert.AreEqual("parameters", root.Name);
        Assert.AreEqual("object", root.Type);
        Assert.AreEqual("{2}", root.Value);
        Assert.IsTrue(root.IsRoot);
        Assert.IsTrue(root.IsExpanded);
        Assert.AreEqual(2, root.Children.Count);
        Assert.AreEqual("array", root.Children.Single(child => child.Name == "items").Type);

        using var copied = JsonDocument.Parse(root.ToCopyText());
        Assert.AreEqual("Alpha", copied.RootElement.GetProperty("name").GetString());
        Assert.AreEqual(4, copied.RootElement.GetProperty("items").GetArrayLength());
        Assert.AreEqual("value", copied.RootElement.GetProperty("items")[3].GetProperty("nested").GetString());
    }

    [TestMethod]
    public void FromJson_ReturnsParseErrorAndRawValueForInvalidJson()
    {
        var nodes = JsonTreeNodeViewModel.FromJson("{ invalid", "empty");

        Assert.AreEqual(2, nodes.Count);
        Assert.AreEqual("parse_error", nodes[0].Name);
        Assert.AreEqual("error", nodes[0].Type);
        Assert.IsTrue(nodes[0].IsExpanded);
        Assert.AreEqual("raw", nodes[1].Name);
        Assert.AreEqual("{ invalid", nodes[1].Value);
    }

    [TestMethod]
    public void ToCopyText_ForArrayItemWritesOnlyTheJsonValue()
    {
        var root = JsonTreeNodeViewModel.FromJson("""{"values":["first","second"]}""", "empty").Single();
        var array = root.Children.Single(child => child.Name == "values");

        Assert.AreEqual("\"second\"", array.Children[1].ToCopyText());
    }

    [TestMethod]
    public void Dispose_DisposesAndClearsChildren()
    {
        var root = JsonTreeNodeViewModel.FromJson("""{"child":{"leaf":1}}""", "empty").Single();
        var child = root.Children.Single();

        root.Dispose();

        Assert.AreEqual(0, root.Children.Count);
        Assert.AreEqual(0, child.Children.Count);
    }
}
