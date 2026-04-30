using System.Text;
using System.Text.Json;
using CodexHelper.Models;

namespace CodexHelper.Services;

public static class ConversationParser
{
    public static IReadOnlyList<ConversationMessage> ParseThreadReadResult(JsonElement result)
    {
        if (result.TryGetProperty("thread", out var thread))
        {
            return ParseThreadObject(thread);
        }

        return ParseThreadObject(result);
    }

    public static IReadOnlyList<ConversationMessage> ParseRolloutLine(JsonElement line)
    {
        var messages = new List<ConversationMessage>();
        if (TryGetString(line, "type", out var type) &&
            type.Equals("response_item", StringComparison.OrdinalIgnoreCase) &&
            line.TryGetProperty("payload", out var payload))
        {
            ParseNode(payload, messages, new HashSet<string>(StringComparer.Ordinal));
        }

        return messages;
    }

    public static IReadOnlyList<ConversationMessage> FilterHiddenInstructionMessages(
        IEnumerable<ConversationMessage> messages,
        out string developerInstructions,
        out string userInstructions)
    {
        var visible = new List<ConversationMessage>();
        var developerBlocks = new List<string>();
        var userBlocks = new List<string>();

        foreach (var message in messages)
        {
            if (IsDeveloperInstructionMessage(message))
            {
                AddDistinct(developerBlocks, message.Content);
                continue;
            }

            if (IsUserInstructionMessage(message))
            {
                AddDistinct(userBlocks, message.Content);
                continue;
            }

            visible.Add(message);
        }

        developerInstructions = string.Join($"{Environment.NewLine}{Environment.NewLine}", developerBlocks);
        userInstructions = string.Join($"{Environment.NewLine}{Environment.NewLine}", userBlocks);
        return visible;
    }

    private static IReadOnlyList<ConversationMessage> ParseThreadObject(JsonElement thread)
    {
        var messages = new List<ConversationMessage>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var propertyName in new[] { "turns", "messages", "items" })
        {
            if (thread.TryGetProperty(propertyName, out var container))
            {
                ParseNode(container, messages, seen);
            }
        }

        if (messages.Count == 0)
        {
            ParseNode(thread, messages, seen);
        }

        return messages;
    }

    private static void ParseNode(JsonElement node, List<ConversationMessage> messages, HashSet<string> seen)
    {
        switch (node.ValueKind)
        {
            case JsonValueKind.Array:
                foreach (var item in node.EnumerateArray())
                {
                    ParseNode(item, messages, seen);
                }
                break;

            case JsonValueKind.Object:
                ParseObject(node, messages, seen);
                break;
        }
    }

    private static void ParseObject(JsonElement obj, List<ConversationMessage> messages, HashSet<string> seen)
    {
        if (TryParseMessageLike(obj, out var message))
        {
            AddMessage(messages, seen, message);
            return;
        }

        if (TryGetString(obj, "type", out var type))
        {
            if (type.Equals("response_item", StringComparison.OrdinalIgnoreCase) &&
                obj.TryGetProperty("payload", out var payload))
            {
                ParseNode(payload, messages, seen);
                return;
            }

            if (TryParseTypedItem(obj, type, out message))
            {
                AddMessage(messages, seen, message);
                return;
            }
        }

        foreach (var propertyName in new[] { "message", "item", "payload", "turn", "turns", "messages", "items", "content", "output" })
        {
            if (obj.TryGetProperty(propertyName, out var child) && child.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
            {
                ParseNode(child, messages, seen);
            }
        }
    }

    private static bool TryParseMessageLike(JsonElement obj, out ConversationMessage message)
    {
        message = null!;

        var hasRole = TryGetString(obj, "role", out var role) ||
            TryGetString(obj, "author", out role) ||
            TryGetString(obj, "sender", out role);

        if (!hasRole)
        {
            return false;
        }

        var content = ExtractTextFromFirstAvailable(obj, "content", "text", "message", "output");
        if (string.IsNullOrWhiteSpace(content))
        {
            return false;
        }

        message = new ConversationMessage(role, content.Trim(), KindFromRole(role));
        return true;
    }

    private static bool TryParseTypedItem(JsonElement obj, string type, out ConversationMessage message)
    {
        message = null!;
        var normalizedType = type.Replace("-", "_", StringComparison.Ordinal).ToLowerInvariant();

        switch (normalizedType)
        {
            case "message":
                return TryParseMessageLike(obj, out message);

            case "reasoning":
            case "reasoning_summary":
                {
                    var content = ExtractTextFromFirstAvailable(obj, "summary", "text", "content");
                    if (string.IsNullOrWhiteSpace(content))
                    {
                        return false;
                    }

                    message = new ConversationMessage("reasoning", content.Trim(), ConversationMessageKind.Reasoning);
                    return true;
                }

            case "function_call":
            case "tool_call":
            case "local_shell_call":
            case "custom_tool_call":
                {
                    var content = BuildToolCallText(obj);
                    if (string.IsNullOrWhiteSpace(content))
                    {
                        return false;
                    }

                    message = new ConversationMessage("tool", content.Trim(), ConversationMessageKind.Tool);
                    return true;
                }

            case "function_call_output":
            case "tool_call_output":
            case "local_shell_call_output":
            case "custom_tool_call_output":
                {
                    var content = ExtractTextFromFirstAvailable(obj, "output", "text", "content");
                    if (string.IsNullOrWhiteSpace(content))
                    {
                        return false;
                    }

                    message = new ConversationMessage("tool", content.Trim(), ConversationMessageKind.Tool);
                    return true;
                }

            case "error":
                {
                    var content = ExtractTextFromFirstAvailable(obj, "message", "text", "content", "error");
                    if (string.IsNullOrWhiteSpace(content))
                    {
                        return false;
                    }

                    message = new ConversationMessage("error", content.Trim(), ConversationMessageKind.Error);
                    return true;
                }
        }

        return false;
    }

    private static string BuildToolCallText(JsonElement obj)
    {
        var builder = new StringBuilder();
        if (TryGetString(obj, "name", out var name) && !string.IsNullOrWhiteSpace(name))
        {
            builder.AppendLine(name);
        }

        foreach (var propertyName in new[] { "command", "code", "arguments", "input" })
        {
            var value = ExtractTextFromFirstAvailable(obj, propertyName);
            if (!string.IsNullOrWhiteSpace(value))
            {
                if (builder.Length > 0)
                {
                    builder.AppendLine();
                }

                builder.Append(value);
            }
        }

        return builder.ToString();
    }

    private static string ExtractTextFromFirstAvailable(JsonElement obj, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            if (obj.TryGetProperty(propertyName, out var value))
            {
                var text = ExtractText(value);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    return text;
                }
            }
        }

        return string.Empty;
    }

    private static string ExtractText(JsonElement value)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.String:
                return value.GetString() ?? string.Empty;

            case JsonValueKind.Number:
            case JsonValueKind.True:
            case JsonValueKind.False:
                return value.ToString();

            case JsonValueKind.Array:
                return string.Join(
                    Environment.NewLine,
                    value.EnumerateArray()
                        .Select(ExtractText)
                        .Where(text => !string.IsNullOrWhiteSpace(text)));

            case JsonValueKind.Object:
                foreach (var propertyName in new[] { "text", "content", "output", "value", "markdown", "summary", "arguments", "command", "code" })
                {
                    if (value.TryGetProperty(propertyName, out var child))
                    {
                        var text = ExtractText(child);
                        if (!string.IsNullOrWhiteSpace(text))
                        {
                            return text;
                        }
                    }
                }

                if (TryGetString(value, "type", out var type) && type.Contains("image", StringComparison.OrdinalIgnoreCase))
                {
                    return "[image]";
                }

                return value.GetRawText();

            default:
                return string.Empty;
        }
    }

    private static void AddMessage(List<ConversationMessage> messages, HashSet<string> seen, ConversationMessage message)
    {
        var signature = $"{message.Kind}|{message.Role}|{message.Content}";
        if (seen.Add(signature))
        {
            messages.Add(message);
        }
    }

    private static ConversationMessageKind KindFromRole(string role)
    {
        return role.ToLowerInvariant() switch
        {
            "user" or "human" => ConversationMessageKind.User,
            "assistant" or "ai" => ConversationMessageKind.Assistant,
            "system" or "developer" => ConversationMessageKind.System,
            "tool" or "function" => ConversationMessageKind.Tool,
            "reasoning" => ConversationMessageKind.Reasoning,
            "error" => ConversationMessageKind.Error,
            _ => ConversationMessageKind.Unknown
        };
    }

    private static bool IsDeveloperInstructionMessage(ConversationMessage message)
    {
        var role = message.Role.ToLowerInvariant();
        if (role == "developer")
        {
            return true;
        }

        return role == "system" &&
            (message.Content.Contains("<permissions instructions>", StringComparison.OrdinalIgnoreCase) ||
             message.Content.Contains("<app-context>", StringComparison.OrdinalIgnoreCase) ||
             message.Content.Contains("<collaboration_mode>", StringComparison.OrdinalIgnoreCase) ||
             message.Content.Contains("You are Codex", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsUserInstructionMessage(ConversationMessage message)
    {
        var role = message.Role.ToLowerInvariant();
        return role == "user" &&
            (message.Content.Contains("# AGENTS.md instructions", StringComparison.OrdinalIgnoreCase) ||
             message.Content.Contains("<environment_context>", StringComparison.OrdinalIgnoreCase) ||
             message.Content.Contains("<INSTRUCTIONS>", StringComparison.OrdinalIgnoreCase));
    }

    private static void AddDistinct(List<string> blocks, string content)
    {
        var trimmed = content.Trim();
        if (trimmed.Length == 0)
        {
            return;
        }

        if (!blocks.Contains(trimmed, StringComparer.Ordinal))
        {
            blocks.Add(trimmed);
        }
    }

    private static bool TryGetString(JsonElement element, string propertyName, out string value)
    {
        value = string.Empty;
        if (!element.TryGetProperty(propertyName, out var property) || property.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return false;
        }

        value = property.ValueKind == JsonValueKind.String ? property.GetString() ?? string.Empty : property.ToString();
        return true;
    }
}
