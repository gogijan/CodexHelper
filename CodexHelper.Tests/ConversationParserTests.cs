using System.Text.Json;
using CodexHelper.Models;
using CodexHelper.Services;

namespace CodexHelper.Tests;

[TestClass]
public sealed class ConversationParserTests
{
    [TestMethod]
    public void ParseThreadReadResult_ParsesNestedTypedItemsAndDeduplicatesMessages()
    {
        const string json = """
            {
              "thread": {
                "turns": [
                  {
                    "message": {
                      "role": "user",
                      "content": [
                        { "text": "Hello" },
                        { "content": "again" }
                      ]
                    }
                  },
                  {
                    "message": {
                      "role": "user",
                      "content": [
                        { "text": "Hello" },
                        { "content": "again" }
                      ]
                    }
                  },
                  {
                    "item": {
                      "type": "reasoning_summary",
                      "summary": "Reasoning summary"
                    }
                  },
                  {
                    "item": {
                      "type": "function_call",
                      "name": "shell",
                      "arguments": "{\"command\":\"dotnet test\"}"
                    }
                  },
                  {
                    "item": {
                      "type": "function_call_output",
                      "output": "Tests passed"
                    }
                  },
                  {
                    "item": {
                      "type": "error",
                      "message": "Something failed"
                    }
                  },
                  {
                    "message": {
                      "role": "assistant",
                      "content": {
                        "type": "input_image"
                      }
                    }
                  }
                ]
              }
            }
            """;

        using var document = JsonDocument.Parse(json);

        var messages = ConversationParser.ParseThreadReadResult(document.RootElement);

        Assert.AreEqual(6, messages.Count);
        AssertMessage(messages[0], ConversationMessageKind.User, "user", $"Hello{Environment.NewLine}again");
        AssertMessage(messages[1], ConversationMessageKind.Reasoning, "reasoning", "Reasoning summary");
        Assert.AreEqual(ConversationMessageKind.Tool, messages[2].Kind);
        StringAssert.Contains(messages[2].Content, "shell");
        StringAssert.Contains(messages[2].Content, "dotnet test");
        AssertMessage(messages[3], ConversationMessageKind.Tool, "tool", "Tests passed");
        AssertMessage(messages[4], ConversationMessageKind.Error, "error", "Something failed");
        AssertMessage(messages[5], ConversationMessageKind.Assistant, "assistant", "[image]");
    }

    [TestMethod]
    public void ParseRolloutLine_ParsesResponseItemPayload()
    {
        const string json = """
            {
              "type": "response_item",
              "payload": {
                "type": "message",
                "role": "assistant",
                "content": "Done"
              }
            }
            """;

        using var document = JsonDocument.Parse(json);

        var messages = ConversationParser.ParseRolloutLine(document.RootElement);

        Assert.AreEqual(1, messages.Count);
        AssertMessage(messages[0], ConversationMessageKind.Assistant, "assistant", "Done");
    }

    [TestMethod]
    public void FilterHiddenInstructionMessages_ReturnsVisibleMessagesAndDistinctInstructionBlocks()
    {
        var messages = new[]
        {
            new ConversationMessage("system", "You are Codex. Follow repository rules.", ConversationMessageKind.System),
            new ConversationMessage("developer", "Developer-only guidance", ConversationMessageKind.System),
            new ConversationMessage("developer", "Developer-only guidance", ConversationMessageKind.System),
            new ConversationMessage("user", "# AGENTS.md instructions\n<INSTRUCTIONS>Use Visual Studio 2026</INSTRUCTIONS>", ConversationMessageKind.User),
            new ConversationMessage("assistant", "Visible answer", ConversationMessageKind.Assistant)
        };

        var visible = ConversationParser.FilterHiddenInstructionMessages(
            messages,
            out var developerInstructions,
            out var userInstructions);

        Assert.AreEqual(1, visible.Count);
        AssertMessage(visible[0], ConversationMessageKind.Assistant, "assistant", "Visible answer");
        Assert.AreEqual(
            $"You are Codex. Follow repository rules.{Environment.NewLine}{Environment.NewLine}Developer-only guidance",
            developerInstructions);
        Assert.AreEqual("# AGENTS.md instructions\n<INSTRUCTIONS>Use Visual Studio 2026</INSTRUCTIONS>", userInstructions);
    }

    private static void AssertMessage(
        ConversationMessage message,
        ConversationMessageKind expectedKind,
        string expectedRole,
        string expectedContent)
    {
        Assert.AreEqual(expectedKind, message.Kind);
        Assert.AreEqual(expectedRole, message.Role);
        Assert.AreEqual(expectedContent, message.Content);
    }
}
