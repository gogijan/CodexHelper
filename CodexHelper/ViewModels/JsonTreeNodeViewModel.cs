using System.Collections.ObjectModel;
using System.Text;
using System.Text.Json;
using CodexHelper.Infrastructure;

namespace CodexHelper.ViewModels;

public sealed class JsonTreeNodeViewModel : ObservableObject, IDisposable
{
    private bool _isExpanded;

    public JsonTreeNodeViewModel(string name, string type, string? value = null, bool isExpanded = false, bool isRoot = false)
    {
        Name = name;
        Type = type;
        Value = value ?? string.Empty;
        _isExpanded = isExpanded;
        IsRoot = isRoot;
    }

    public string Name { get; }

    public string Type { get; }

    public string Value { get; }

    public bool HasValue => !string.IsNullOrWhiteSpace(Value);

    public bool IsRoot { get; }

    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetProperty(ref _isExpanded, value);
    }

    public ObservableCollection<JsonTreeNodeViewModel> Children { get; } = new();

    public string ToCopyText()
    {
        var builder = new StringBuilder();
        if (IsRoot || IsArrayItemName(Name))
        {
            WriteJsonValue(builder, this, 0);
        }
        else
        {
            WriteJsonProperty(builder, this, 0);
        }

        return builder.ToString();
    }

    public static IReadOnlyList<JsonTreeNodeViewModel> FromJson(string json, string emptyText)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [new JsonTreeNodeViewModel(emptyText, "empty")];
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            return [CreateNode("parameters", document.RootElement, isRoot: true)];
        }
        catch (JsonException ex)
        {
            return
            [
                new JsonTreeNodeViewModel("parse_error", "error", ex.Message, isExpanded: true),
                new JsonTreeNodeViewModel("raw", "string", json)
            ];
        }
    }

    private static JsonTreeNodeViewModel CreateNode(string name, JsonElement element, bool isRoot = false)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Object => CreateObjectNode(name, element, isRoot),
            JsonValueKind.Array => CreateArrayNode(name, element, isRoot),
            JsonValueKind.String => new JsonTreeNodeViewModel(name, "string", element.GetString() ?? string.Empty, isRoot: isRoot),
            JsonValueKind.Number => new JsonTreeNodeViewModel(name, "number", element.GetRawText(), isRoot: isRoot),
            JsonValueKind.True => new JsonTreeNodeViewModel(name, "bool", "true", isRoot: isRoot),
            JsonValueKind.False => new JsonTreeNodeViewModel(name, "bool", "false", isRoot: isRoot),
            JsonValueKind.Null => new JsonTreeNodeViewModel(name, "null", "null", isRoot: isRoot),
            _ => new JsonTreeNodeViewModel(name, element.ValueKind.ToString().ToLowerInvariant(), element.GetRawText(), isRoot: isRoot)
        };
    }

    private static JsonTreeNodeViewModel CreateObjectNode(string name, JsonElement element, bool isRoot)
    {
        var propertyCount = element.EnumerateObject().Count();
        var node = new JsonTreeNodeViewModel(name, "object", $"{{{propertyCount}}}", isExpanded: isRoot, isRoot: isRoot);

        foreach (var property in element.EnumerateObject())
        {
            node.Children.Add(CreateNode(property.Name, property.Value));
        }

        return node;
    }

    private static JsonTreeNodeViewModel CreateArrayNode(string name, JsonElement element, bool isRoot)
    {
        var itemCount = element.EnumerateArray().Count();
        var node = new JsonTreeNodeViewModel(name, "array", $"[{itemCount}]", isExpanded: isRoot, isRoot: isRoot);

        var index = 0;
        foreach (var item in element.EnumerateArray())
        {
            node.Children.Add(CreateNode($"[{index}]", item));
            index++;
        }

        return node;
    }

    public void Dispose()
    {
        foreach (var child in Children)
        {
            child.Dispose();
        }

        Children.Clear();
    }

    private static void WriteJsonProperty(StringBuilder builder, JsonTreeNodeViewModel node, int indent)
    {
        AppendIndent(builder, indent);
        builder.Append(JsonSerializer.Serialize(node.Name));
        builder.Append(": ");
        WriteJsonValue(builder, node, indent);
    }

    private static void WriteJsonValue(StringBuilder builder, JsonTreeNodeViewModel node, int indent)
    {
        switch (node.Type)
        {
            case "object":
                WriteObject(builder, node, indent);
                break;
            case "array":
                WriteArray(builder, node, indent);
                break;
            case "string":
                builder.Append(JsonSerializer.Serialize(node.Value));
                break;
            case "number":
            case "bool":
            case "null":
                builder.Append(string.IsNullOrWhiteSpace(node.Value) ? "null" : node.Value);
                break;
            default:
                builder.Append(JsonSerializer.Serialize(node.HasValue ? node.Value : node.Name));
                break;
        }
    }

    private static void WriteObject(StringBuilder builder, JsonTreeNodeViewModel node, int indent)
    {
        if (node.Children.Count == 0)
        {
            builder.Append("{}");
            return;
        }

        builder.AppendLine("{");
        for (var index = 0; index < node.Children.Count; index++)
        {
            WriteJsonProperty(builder, node.Children[index], indent + 1);
            if (index < node.Children.Count - 1)
            {
                builder.Append(',');
            }

            builder.AppendLine();
        }

        AppendIndent(builder, indent);
        builder.Append('}');
    }

    private static void WriteArray(StringBuilder builder, JsonTreeNodeViewModel node, int indent)
    {
        if (node.Children.Count == 0)
        {
            builder.Append("[]");
            return;
        }

        builder.AppendLine("[");
        for (var index = 0; index < node.Children.Count; index++)
        {
            AppendIndent(builder, indent + 1);
            WriteJsonValue(builder, node.Children[index], indent + 1);
            if (index < node.Children.Count - 1)
            {
                builder.Append(',');
            }

            builder.AppendLine();
        }

        AppendIndent(builder, indent);
        builder.Append(']');
    }

    private static void AppendIndent(StringBuilder builder, int level)
    {
        builder.Append(' ', level * 2);
    }

    private static bool IsArrayItemName(string name)
    {
        return name.Length > 2 &&
            name[0] == '[' &&
            name[^1] == ']' &&
            name[1..^1].All(char.IsDigit);
    }
}
