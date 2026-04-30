using System.Text.Json;
using CodexHelper.Models;
using CodexHelper.Services;

namespace CodexHelper.Tests;

[TestClass]
public sealed class RolloutThreadReaderTests
{
    [TestMethod]
    public async Task TryReadDetailsAsync_ReadsRolloutMetadataMessagesAndSanitizedParameters()
    {
        using var temp = TempDirectory.Create();
        var rolloutPath = Path.Combine(temp.DirectoryPath, "rollout-thread-1.jsonl");
        await File.WriteAllLinesAsync(
            rolloutPath,
            new[]
            {
                """
                {"type":"session_meta","payload":{"id":"thread-1","timestamp":"2026-04-30T12:00:00Z","name":"Thread name","cwd":"C:\\Work"}}
                """,
                """
                {"type":"turn_context","timestamp":"2026-04-30T12:01:00Z","payload":{"model":"gpt-5.2","effort":"high","developer_instructions":"Dev block","user_instructions":"User block","base_instructions":"Remove this","collaboration_mode":{"settings":{"model":"gpt-5.3","reasoning_effort":"xhigh","developer_instructions":"Nested dev block"}}}}
                """,
                """
                {"type":"event_msg","payload":{"type":"token_count","info":{"model_context_window":"200000","instructions_seen":"Remove this too"}}}
                """,
                """
                {"type":"response_item","payload":{"type":"message","role":"system","content":"You are Codex. Hidden developer instructions."}}
                """,
                """
                {"type":"response_item","payload":{"type":"message","role":"user","content":"Open the old Codex thread"}}
                """,
                """
                {"type":"response_item","payload":{"type":"message","role":"assistant","content":[{"text":"Opened"},{"content":"ready"}]}}
                """
            });

        var thread = new CodexThread
        {
            Id = "thread-1",
            Name = "Thread name",
            Cwd = @"C:\Work",
            Path = rolloutPath,
            UpdatedAt = DateTimeOffset.Parse("2026-04-30T12:02:00Z")
        };

        var details = await new RolloutThreadReader().TryReadDetailsAsync(thread);

        Assert.AreEqual(DateTimeOffset.Parse("2026-04-30T12:00:00Z"), details.Timestamp);
        Assert.AreEqual("gpt-5.3", details.Model);
        Assert.AreEqual("xhigh", details.Effort);
        Assert.AreEqual(200000, details.ModelContextWindow);
        Assert.AreEqual("Dev block", details.DeveloperInstructions);
        Assert.AreEqual("User block", details.UserInstructions);

        Assert.AreEqual(2, details.Messages.Count);
        Assert.AreEqual(ConversationMessageKind.User, details.Messages[0].Kind);
        Assert.AreEqual("Open the old Codex thread", details.Messages[0].Content);
        Assert.AreEqual(ConversationMessageKind.Assistant, details.Messages[1].Kind);
        Assert.AreEqual($"Opened{Environment.NewLine}ready", details.Messages[1].Content);

        using var parameters = JsonDocument.Parse(details.Parameters);
        var root = parameters.RootElement;
        Assert.AreEqual("thread-1", root.GetProperty("thread").GetProperty("id").GetString());
        Assert.AreEqual("gpt-5.3", root.GetProperty("latest_turn_context").GetProperty("collaboration_mode").GetProperty("settings").GetProperty("model").GetString());
        Assert.AreEqual("200000", root.GetProperty("latest_token_count").GetProperty("info").GetProperty("model_context_window").GetString());
        Assert.IsFalse(details.Parameters.Contains("developer_instructions", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(details.Parameters.Contains("user_instructions", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(details.Parameters.Contains("base_instructions", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(details.Parameters.Contains("instructions_seen", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public async Task TryReadDetailsAsync_ReturnsParametersWhenRolloutPathDoesNotExist()
    {
        var missingPath = Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.jsonl");
        var thread = new CodexThread
        {
            Id = "missing-thread",
            Name = "Missing",
            Path = missingPath
        };

        var details = await new RolloutThreadReader().TryReadDetailsAsync(thread);

        Assert.AreEqual(0, details.Messages.Count);
        StringAssert.Contains(details.Parameters, "missing-thread");
        Assert.IsNull(details.Model);
        Assert.IsNull(details.Effort);
    }
}
