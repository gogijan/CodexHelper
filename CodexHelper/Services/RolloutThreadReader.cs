using System.IO;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using CodexHelper.Models;

namespace CodexHelper.Services;

public sealed class RolloutThreadReader
{
    public async Task<ThreadDetails> TryReadDetailsAsync(
        CodexThread thread,
        CancellationToken cancellationToken = default,
        bool includeMessages = true)
    {
        var path = await ResolvePathAsync(thread, cancellationToken);
        if (path is null)
        {
            return new ThreadDetails
            {
                Parameters = BuildParametersText(thread, null, null, null, null)
            };
        }

        var messages = new List<ConversationMessage>();
        DateTimeOffset? timestamp = null;
        string? model = null;
        string? effort = null;
        long? modelContextWindow = null;
        string? developerInstructions = null;
        string? userInstructions = null;
        JsonNode? sessionMeta = null;
        JsonNode? latestTurnContext = null;
        JsonNode? latestTokenCount = null;

        await using var stream = OpenSharedRead(path);
        using var reader = new StreamReader(stream);
        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            try
            {
                using var document = JsonDocument.Parse(line);
                var root = document.RootElement;
                if (includeMessages)
                {
                    messages.AddRange(ConversationParser.ParseRolloutLine(root));
                }

                if (!TryGetString(root, "type", out var type) || !root.TryGetProperty("payload", out var payload))
                {
                    continue;
                }

                if (type.Equals("session_meta", StringComparison.OrdinalIgnoreCase))
                {
                    timestamp ??= TryGetDateTimeOffset(payload, "timestamp");
                    sessionMeta = SanitizeJson(payload);
                }
                else if (type.Equals("turn_context", StringComparison.OrdinalIgnoreCase))
                {
                    timestamp ??= TryGetDateTimeOffset(root, "timestamp");
                    latestTurnContext = SanitizeJson(payload);
                    model = TryGetString(payload, "model", out var turnModel) ? turnModel : model;
                    effort = TryGetString(payload, "effort", out var turnEffort) ? turnEffort : effort;
                    developerInstructions = TryGetString(payload, "developer_instructions", out var dev)
                        ? dev
                        : developerInstructions;
                    userInstructions = TryGetString(payload, "user_instructions", out var user)
                        ? user
                        : userInstructions;

                    if (payload.TryGetProperty("collaboration_mode", out var collaborationMode) &&
                        collaborationMode.TryGetProperty("settings", out var settings))
                    {
                        model = TryGetString(settings, "model", out var settingsModel) ? settingsModel : model;
                        effort = TryGetString(settings, "reasoning_effort", out var settingsEffort) ? settingsEffort : effort;
                        developerInstructions = string.IsNullOrWhiteSpace(developerInstructions) &&
                            TryGetString(settings, "developer_instructions", out var settingsDeveloperInstructions)
                                ? settingsDeveloperInstructions
                                : developerInstructions;
                    }
                }
                else if (type.Equals("event_msg", StringComparison.OrdinalIgnoreCase) &&
                    payload.TryGetProperty("type", out var eventType) &&
                    eventType.GetString()?.Equals("token_count", StringComparison.OrdinalIgnoreCase) == true)
                {
                    latestTokenCount = SanitizeJson(payload);
                    if (payload.TryGetProperty("info", out var tokenInfo) &&
                        TryGetInt64(tokenInfo, "model_context_window", out var contextWindow))
                    {
                        modelContextWindow = contextWindow;
                    }
                }
            }
            catch
            {
            }
        }

        IReadOnlyList<ConversationMessage> visibleMessages = Array.Empty<ConversationMessage>();
        var parsedDeveloperInstructions = string.Empty;
        var parsedUserInstructions = string.Empty;
        if (includeMessages)
        {
            visibleMessages = ConversationParser.FilterHiddenInstructionMessages(
                messages,
                out parsedDeveloperInstructions,
                out parsedUserInstructions);
        }

        return new ThreadDetails
        {
            Messages = visibleMessages,
            Timestamp = timestamp,
            Model = model,
            Effort = effort,
            ModelContextWindow = modelContextWindow,
            DeveloperInstructions = FirstNonEmpty(developerInstructions, parsedDeveloperInstructions),
            UserInstructions = FirstNonEmpty(userInstructions, parsedUserInstructions),
            Parameters = BuildParametersText(thread, path, sessionMeta, latestTurnContext, latestTokenCount)
        };
    }

    private static FileStream OpenSharedRead(string path)
    {
        try
        {
            return new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                bufferSize: 64 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
        }
        catch (IOException ex) when (IsFileLockException(ex))
        {
            throw new ThreadFileLockedException(path, ex);
        }
    }

    public async Task<IReadOnlyList<ConversationMessage>> TryReadMessagesAsync(CodexThread thread, CancellationToken cancellationToken = default)
    {
        return (await TryReadDetailsAsync(thread, cancellationToken)).Messages;
    }

    private static async Task<string?> ResolvePathAsync(CodexThread thread, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(thread.Path) && File.Exists(thread.Path))
        {
            return thread.Path;
        }

        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var roots = new[]
        {
            Path.Combine(userProfile, ".codex", "sessions"),
            Path.Combine(userProfile, ".codex", "archived_sessions")
        };

        foreach (var root in roots)
        {
            if (!Directory.Exists(root))
            {
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(root, "rollout-*.jsonl", SearchOption.AllDirectories))
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (Path.GetFileName(file).Contains(thread.Id, StringComparison.OrdinalIgnoreCase))
                {
                    return file;
                }

                var firstLine = await ReadFirstLineAsync(file, cancellationToken);
                if (firstLine?.Contains(thread.Id, StringComparison.OrdinalIgnoreCase) == true)
                {
                    return file;
                }
            }
        }

        return null;
    }

    private static async Task<string?> ReadFirstLineAsync(string file, CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = OpenSharedRead(file);
            using var reader = new StreamReader(stream);
            return await reader.ReadLineAsync(cancellationToken);
        }
        catch
        {
            return null;
        }
    }

    private static bool IsFileLockException(IOException exception)
    {
        var errorCode = exception.HResult & 0xFFFF;
        return errorCode is 32 or 33;
    }

    private static DateTimeOffset? TryGetDateTimeOffset(JsonElement element, string propertyName)
    {
        if (!TryGetString(element, propertyName, out var value))
        {
            return null;
        }

        return DateTimeOffset.TryParse(value, out var timestamp) ? timestamp : null;
    }

    private static bool TryGetString(JsonElement element, string propertyName, out string value)
    {
        value = string.Empty;
        if (!element.TryGetProperty(propertyName, out var property) ||
            property.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return false;
        }

        value = property.ValueKind == JsonValueKind.String ? property.GetString() ?? string.Empty : property.ToString();
        return !string.IsNullOrWhiteSpace(value);
    }

    private static bool TryGetInt64(JsonElement element, string propertyName, out long value)
    {
        value = 0;
        if (!element.TryGetProperty(propertyName, out var property) ||
            property.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return false;
        }

        if (property.ValueKind == JsonValueKind.Number && property.TryGetInt64(out value))
        {
            return true;
        }

        return property.ValueKind == JsonValueKind.String &&
            long.TryParse(property.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;
    }

    private static JsonNode? SanitizeJson(JsonElement element)
    {
        var node = JsonNode.Parse(element.GetRawText());
        RemoveInstructionProperties(node);
        return node;
    }

    private static void RemoveInstructionProperties(JsonNode? node)
    {
        if (node is JsonObject obj)
        {
            foreach (var property in obj.ToArray())
            {
                if (ShouldRemoveProperty(property.Key))
                {
                    obj.Remove(property.Key);
                    continue;
                }

                RemoveInstructionProperties(property.Value);
            }
        }
        else if (node is JsonArray array)
        {
            foreach (var item in array)
            {
                RemoveInstructionProperties(item);
            }
        }
    }

    private static bool ShouldRemoveProperty(string propertyName)
    {
        return propertyName.Contains("instructions", StringComparison.OrdinalIgnoreCase) ||
            propertyName.Equals("base_instructions", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildParametersText(
        CodexThread thread,
        string? rolloutPath,
        JsonNode? sessionMeta,
        JsonNode? latestTurnContext,
        JsonNode? latestTokenCount)
    {
        var root = new JsonObject
        {
            ["thread"] = new JsonObject
            {
                ["id"] = thread.Id,
                ["name"] = thread.Name,
                ["cwd"] = thread.Cwd,
                ["path"] = thread.Path,
                ["rollout_path"] = rolloutPath,
                ["updated_at"] = thread.UpdatedAt?.ToString("O"),
                ["archived"] = thread.IsArchived
            }
        };

        if (sessionMeta is not null)
        {
            root["session_meta"] = sessionMeta;
        }

        if (latestTurnContext is not null)
        {
            root["latest_turn_context"] = latestTurnContext;
        }

        if (latestTokenCount is not null)
        {
            root["latest_token_count"] = latestTokenCount;
        }

        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }
}
