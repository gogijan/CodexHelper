using System.IO;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using CodexHelper.Models;

namespace CodexHelper.Services;

public sealed class RolloutThreadReader
{
    private const int CacheVersion = 1;

    private static readonly JsonSerializerOptions CacheJsonOptions = new()
    {
        WriteIndented = true
    };

    private static readonly string CacheDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CodexHelper");

    public static string CachePath { get; } = Path.Combine(CacheDirectory, "rollout-index.json");

    private readonly SemaphoreSlim _indexLock = new(1, 1);
    private readonly object _invalidationGate = new();
    private readonly HashSet<string> _pendingChangedPaths = new(StringComparer.OrdinalIgnoreCase);
    private RolloutIndex? _index;

    public async Task<IReadOnlyList<CodexThread>> GetAllThreadsAsync(CancellationToken cancellationToken = default)
    {
        return (await EnsureIndexAsync(cancellationToken)).Threads;
    }

    public void InvalidateCache(IReadOnlyList<string>? changedPaths = null)
    {
        lock (_invalidationGate)
        {
            if (changedPaths is null)
            {
                _pendingChangedPaths.Clear();
                _index = null;
                return;
            }

            if (changedPaths.Count == 0 || _index is null)
            {
                return;
            }

            foreach (var path in changedPaths.Where(IsRolloutFile))
            {
                _pendingChangedPaths.Add(path);
            }
        }
    }

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

    private async Task<string?> ResolvePathAsync(CodexThread thread, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(thread.Path) && File.Exists(thread.Path))
        {
            return thread.Path;
        }

        var index = await EnsureIndexAsync(cancellationToken);
        if (index.PathsByThreadId.TryGetValue(thread.Id, out var cachedPath) && File.Exists(cachedPath))
        {
            return cachedPath;
        }

        return null;
    }

    private async Task<RolloutIndex> EnsureIndexAsync(CancellationToken cancellationToken)
    {
        if (_index is { } existing && !HasPendingChangedPaths())
        {
            return existing;
        }

        await _indexLock.WaitAsync(cancellationToken);
        try
        {
            if (_index is { } lockedExisting)
            {
                var changedPaths = TakePendingChangedPaths();
                if (changedPaths.Count == 0)
                {
                    return lockedExisting;
                }

                _index = await ApplyIndexChangesAsync(lockedExisting, changedPaths, cancellationToken);
                return _index;
            }

            _index = await BuildIndexAsync(cancellationToken);
            return _index;
        }
        finally
        {
            _indexLock.Release();
        }
    }

    private bool HasPendingChangedPaths()
    {
        lock (_invalidationGate)
        {
            return _pendingChangedPaths.Count > 0;
        }
    }

    private IReadOnlyList<string> TakePendingChangedPaths()
    {
        lock (_invalidationGate)
        {
            if (_pendingChangedPaths.Count == 0)
            {
                return Array.Empty<string>();
            }

            var changedPaths = _pendingChangedPaths.ToArray();
            _pendingChangedPaths.Clear();
            return changedPaths;
        }
    }

    private static async Task<RolloutIndex> BuildIndexAsync(CancellationToken cancellationToken)
    {
        var entriesByPath = new Dictionary<string, CodexThread>(StringComparer.OrdinalIgnoreCase);
        var cacheEntries = new List<RolloutIndexCacheEntry>();
        var cachedEntries = LoadDiskCache()
            .Entries
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Path))
            .GroupBy(entry => entry.Path, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        foreach (var root in GetSessionRoots())
        {
            if (!Directory.Exists(root.Path))
            {
                continue;
            }

            foreach (var file in EnumerateRolloutFiles(root.Path, cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();

                var fileInfo = new FileInfo(file);
                var thread = TryGetCachedThread(cachedEntries, fileInfo, root.Archived)
                    ?? await TryReadIndexThreadAsync(file, root.Archived, cancellationToken);
                if (thread is null)
                {
                    continue;
                }

                entriesByPath[file] = thread;
                cacheEntries.Add(CreateCacheEntry(thread, fileInfo));
            }
        }

        SaveDiskCache(cacheEntries);
        var index = CreateIndex(entriesByPath);
        DiagnosticLogService.Info($"Built rollout index with {index.Threads.Count} thread(s).");
        return index;
    }

    private static async Task<RolloutIndex> ApplyIndexChangesAsync(
        RolloutIndex current,
        IReadOnlyList<string> changedPaths,
        CancellationToken cancellationToken)
    {
        var entriesByPath = new Dictionary<string, CodexThread>(
            current.EntriesByPath,
            StringComparer.OrdinalIgnoreCase);

        foreach (var path in changedPaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsRolloutFile(path))
            {
                continue;
            }

            entriesByPath.Remove(path);
            if (!File.Exists(path))
            {
                continue;
            }

            var root = FindSessionRoot(path);
            if (root is null)
            {
                continue;
            }

            var thread = await TryReadIndexThreadAsync(path, root.Archived, cancellationToken);
            if (thread is not null)
            {
                entriesByPath[path] = thread;
            }
        }

        var index = CreateIndex(entriesByPath);
        SaveDiskCache(CreateCacheEntries(index.EntriesByPath));
        DiagnosticLogService.Info($"Updated rollout index incrementally for {changedPaths.Count} changed path(s).");
        return index;
    }

    private static RolloutIndexCache LoadDiskCache()
    {
        try
        {
            if (!File.Exists(CachePath))
            {
                return new RolloutIndexCache();
            }

            using var stream = File.OpenRead(CachePath);
            var cache = JsonSerializer.Deserialize<RolloutIndexCache>(stream, CacheJsonOptions);
            return cache?.Version == CacheVersion ? cache : new RolloutIndexCache();
        }
        catch (Exception ex)
        {
            DiagnosticLogService.Warning($"Could not read rollout index cache '{CachePath}'.", ex);
            return new RolloutIndexCache();
        }
    }

    private static void SaveDiskCache(IReadOnlyList<RolloutIndexCacheEntry> entries)
    {
        try
        {
            Directory.CreateDirectory(CacheDirectory);
            using var stream = File.Create(CachePath);
            JsonSerializer.Serialize(
                stream,
                new RolloutIndexCache
                {
                    Version = CacheVersion,
                    Entries = entries.ToList()
                },
                CacheJsonOptions);
        }
        catch (Exception ex)
        {
            DiagnosticLogService.Warning($"Could not write rollout index cache '{CachePath}'.", ex);
        }
    }

    private static RolloutIndex CreateIndex(IReadOnlyDictionary<string, CodexThread> entriesByPath)
    {
        var threads = entriesByPath.Values
            .GroupBy(thread => thread.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderBy(thread => thread.IsArchived).First())
            .OrderByDescending(thread => thread.UpdatedAt)
            .ToArray();

        var pathsByThreadId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var thread in threads)
        {
            if (!string.IsNullOrWhiteSpace(thread.Id) &&
                !string.IsNullOrWhiteSpace(thread.Path))
            {
                pathsByThreadId[thread.Id] = thread.Path;
            }
        }

        return new RolloutIndex(
            threads,
            pathsByThreadId,
            new Dictionary<string, CodexThread>(entriesByPath, StringComparer.OrdinalIgnoreCase));
    }

    private static IReadOnlyList<RolloutIndexCacheEntry> CreateCacheEntries(
        IReadOnlyDictionary<string, CodexThread> entriesByPath)
    {
        var entries = new List<RolloutIndexCacheEntry>(entriesByPath.Count);
        foreach (var (path, thread) in entriesByPath)
        {
            try
            {
                if (File.Exists(path))
                {
                    entries.Add(CreateCacheEntry(thread, new FileInfo(path)));
                }
            }
            catch (Exception ex)
            {
                DiagnosticLogService.Warning($"Could not cache rollout index entry for '{path}'.", ex);
            }
        }

        return entries;
    }

    private static IReadOnlyList<SessionRoot> GetSessionRoots()
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var codexHome = Path.Combine(userProfile, ".codex");
        return
        [
            new SessionRoot(Path.Combine(codexHome, "sessions"), Archived: false),
            new SessionRoot(Path.Combine(codexHome, "archived_sessions"), Archived: true)
        ];
    }

    private static SessionRoot? FindSessionRoot(string path)
    {
        foreach (var root in GetSessionRoots().OrderByDescending(root => root.Path.Length))
        {
            if (IsPathUnderRoot(path, root.Path))
            {
                return root;
            }
        }

        return null;
    }

    private static bool IsPathUnderRoot(string path, string root)
    {
        try
        {
            var fullPath = Path.GetFullPath(path);
            var fullRoot = Path.GetFullPath(root)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
                Path.DirectorySeparatorChar;

            return fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    private static IEnumerable<string> EnumerateRolloutFiles(string root, CancellationToken cancellationToken)
    {
        var pending = new Stack<string>();
        pending.Push(root);

        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var directory = pending.Pop();

            foreach (var file in EnumerateFilesSafe(directory))
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return file;
            }

            foreach (var childDirectory in EnumerateDirectoriesSafe(directory))
            {
                pending.Push(childDirectory);
            }
        }
    }

    private static IReadOnlyList<string> EnumerateFilesSafe(string directory)
    {
        try
        {
            return Directory.EnumerateFiles(directory, "rollout-*.jsonl", SearchOption.TopDirectoryOnly).ToArray();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            DiagnosticLogService.Warning($"Could not enumerate rollout files under '{directory}'.", ex);
            return Array.Empty<string>();
        }
    }

    private static IReadOnlyList<string> EnumerateDirectoriesSafe(string directory)
    {
        try
        {
            return Directory.EnumerateDirectories(directory).ToArray();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            DiagnosticLogService.Warning($"Could not enumerate rollout subdirectories under '{directory}'.", ex);
            return Array.Empty<string>();
        }
    }

    private static CodexThread? TryGetCachedThread(
        IReadOnlyDictionary<string, RolloutIndexCacheEntry> entries,
        FileInfo fileInfo,
        bool archived)
    {
        if (!entries.TryGetValue(fileInfo.FullName, out var entry) ||
            entry.Archived != archived ||
            entry.Length != fileInfo.Length ||
            entry.LastWriteTimeUtc != fileInfo.LastWriteTimeUtc ||
            string.IsNullOrWhiteSpace(entry.Id))
        {
            return null;
        }

        return new CodexThread
        {
            Id = entry.Id,
            Name = string.IsNullOrWhiteSpace(entry.Name) ? Path.GetFileNameWithoutExtension(fileInfo.Name) : entry.Name,
            Cwd = entry.Cwd,
            Path = fileInfo.FullName,
            UpdatedAt = entry.UpdatedAt,
            IsArchived = entry.Archived,
            IsChat = entry.IsChat
        };
    }

    private static RolloutIndexCacheEntry CreateCacheEntry(CodexThread thread, FileInfo fileInfo)
    {
        return new RolloutIndexCacheEntry
        {
            Path = fileInfo.FullName,
            Length = fileInfo.Length,
            LastWriteTimeUtc = fileInfo.LastWriteTimeUtc,
            Id = thread.Id,
            Name = thread.Name,
            Cwd = thread.Cwd,
            UpdatedAt = thread.UpdatedAt,
            Archived = thread.IsArchived,
            IsChat = thread.IsChat
        };
    }

    private static async Task<CodexThread?> TryReadIndexThreadAsync(
        string file,
        bool archived,
        CancellationToken cancellationToken)
    {
        var id = string.Empty;
        var name = string.Empty;
        string? cwd = null;

        try
        {
            await using var stream = OpenSharedRead(file);
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
                    if (TryGetString(root, "payload", "id", out var payloadId) ||
                        TryGetString(root, "payload", "session_id", out payloadId))
                    {
                        id = FirstNonEmpty(id, payloadId);
                    }

                    if (TryGetString(root, "payload", "cwd", out var payloadCwd) ||
                        TryGetString(root, "payload", "workdir", out payloadCwd) ||
                        TryGetString(root, "payload", "working_directory", out payloadCwd))
                    {
                        cwd ??= payloadCwd;
                    }

                    if (TryGetString(root, "payload", "name", out var payloadName) ||
                        TryGetString(root, "payload", "title", out payloadName))
                    {
                        name = FirstNonEmpty(name, payloadName);
                    }

                    if (string.IsNullOrWhiteSpace(name))
                    {
                        var messages = ConversationParser.ParseRolloutLine(root);
                        var firstUserMessage = messages.FirstOrDefault(message => message.Kind == ConversationMessageKind.User);
                        if (firstUserMessage is not null)
                        {
                            name = BuildThreadName(firstUserMessage.Content);
                        }
                    }

                    if (!string.IsNullOrWhiteSpace(id) &&
                        !string.IsNullOrWhiteSpace(name) &&
                        !string.IsNullOrWhiteSpace(cwd))
                    {
                        break;
                    }
                }
                catch
                {
                }
            }
        }
        catch (ThreadFileLockedException ex)
        {
            DiagnosticLogService.Warning($"Could not index locked rollout file '{file}'.", ex);
            return null;
        }
        catch (Exception ex)
        {
            DiagnosticLogService.Warning($"Could not index rollout file '{file}'.", ex);
            return null;
        }

        if (string.IsNullOrWhiteSpace(id))
        {
            id = Path.GetFileNameWithoutExtension(file);
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            name = Path.GetFileNameWithoutExtension(file);
        }

        return new CodexThread
        {
            Id = id,
            Name = name,
            Cwd = cwd,
            Path = file,
            UpdatedAt = File.GetLastWriteTimeUtc(file),
            IsArchived = archived,
            IsChat = IsChatCwd(cwd)
        };
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

    private static bool TryGetString(JsonElement element, string parentPropertyName, string propertyName, out string value)
    {
        value = string.Empty;
        return element.TryGetProperty(parentPropertyName, out var parent) &&
            parent.ValueKind == JsonValueKind.Object &&
            TryGetString(parent, propertyName, out value);
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

    private static string BuildThreadName(string content)
    {
        var name = string.Join(
            " ",
            content.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        return name.Length <= 80 ? name : $"{name[..77]}...";
    }

    private static bool IsChatCwd(string? cwd)
    {
        if (string.IsNullOrWhiteSpace(cwd))
        {
            return false;
        }

        var segments = cwd
            .Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        for (var index = 0; index < segments.Length - 1; index++)
        {
            if (segments[index].Equals("Codex", StringComparison.OrdinalIgnoreCase) &&
                IsDateFolder(segments[index + 1]))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsDateFolder(string value)
    {
        return value.Length == 10 &&
            char.IsDigit(value[0]) &&
            char.IsDigit(value[1]) &&
            char.IsDigit(value[2]) &&
            char.IsDigit(value[3]) &&
            value[4] == '-' &&
            char.IsDigit(value[5]) &&
            char.IsDigit(value[6]) &&
            value[7] == '-' &&
            char.IsDigit(value[8]) &&
            char.IsDigit(value[9]);
    }

    private static bool IsRolloutFile(string path)
    {
        var fileName = Path.GetFileName(path);
        return fileName.StartsWith("rollout-", StringComparison.OrdinalIgnoreCase) &&
            fileName.EndsWith(".jsonl", StringComparison.OrdinalIgnoreCase);
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

    private sealed record RolloutIndex(
        IReadOnlyList<CodexThread> Threads,
        IReadOnlyDictionary<string, string> PathsByThreadId,
        IReadOnlyDictionary<string, CodexThread> EntriesByPath);

    private sealed record SessionRoot(string Path, bool Archived);

    private sealed class RolloutIndexCache
    {
        public int Version { get; set; } = CacheVersion;

        public List<RolloutIndexCacheEntry> Entries { get; set; } = [];
    }

    private sealed class RolloutIndexCacheEntry
    {
        public string Path { get; set; } = string.Empty;

        public long Length { get; set; }

        public DateTime LastWriteTimeUtc { get; set; }

        public string Id { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        public string? Cwd { get; set; }

        public DateTimeOffset? UpdatedAt { get; set; }

        public bool Archived { get; set; }

        public bool IsChat { get; set; }
    }
}
