using System.Reflection;
using System.Text.Json;
using Birko.AI.Models;
using Birko.AI.Providers;
using Birko.AI.Tools;
using FluentAssertions;
using Xunit;

namespace Birko.AI.Providers.Tests;

/// <summary>
/// TASK-546: a hook's <c>AppendUserMessage</c> after a tool batch puts a text user turn right after the tool-result
/// user turn. Each provider's request must be valid for that shape under its own contract:
/// Gemini (roles alternate) merges the two into one content; Mistral (no user right after a tool message) gets a
/// placeholder assistant message between them; OpenAI-style providers and Claude accept it and pass it through.
/// The agent's conversation is never changed — only the request.
/// </summary>
public class ProviderTurnOrderTests
{
    // user → assistant(tool_use) → user(tool_result) → user(text, from a hook)
    private static List<Message> HookAfterToolBatch() => new()
    {
        new() { Role = "user", Content = "do it" },
        new()
        {
            Role = "assistant",
            Content = new List<ContentBlock>
            {
                new() { Type = "tool_use", Id = "t1", Name = "record", Input = new Dictionary<string, object>() }
            }
        },
        new()
        {
            Role = "user",
            Content = new List<object> { new { type = "tool_result", tool_use_id = "t1", content = "done" } }
        },
        new() { Role = "user", Content = "steer" }
    };

    private static JsonElement OpenAiStylePayload(OpenAiCompatibleProviderBase provider, List<Message> messages, bool streaming)
    {
        var method = typeof(OpenAiCompatibleProviderBase)
            .GetMethod(streaming ? "BuildStreamingPayload" : "BuildPayload", BindingFlags.NonPublic | BindingFlags.Instance)!;
        return JsonSerializer.SerializeToElement(method.Invoke(provider, new object[] { messages, new List<Tool>(), "system" })!);
    }

    private static JsonElement StaticPayload(Type provider, params object[] args)
    {
        var method = provider.GetMethod("BuildRequestPayload", BindingFlags.NonPublic | BindingFlags.Static)!;
        return JsonSerializer.SerializeToElement(method.Invoke(null, args)!);
    }

    private static string?[] Roles(JsonElement array)
        => array.EnumerateArray().Select(m => m.GetProperty("role").GetString()).ToArray();

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Mistral_PutsAPlaceholderAssistantBetweenToolAndUser(bool streaming)
    {
        var messages = HookAfterToolBatch();
        var payload = OpenAiStylePayload(new MistralProvider("key"), messages, streaming).GetProperty("messages");

        Roles(payload).Should().Equal("system", "user", "assistant", "tool", "assistant", "user");
        var placeholder = payload[4];
        placeholder.GetProperty("content").GetString().Should().Be(" ", "Mistral does not accept an empty assistant message");
        payload[5].GetProperty("content").GetString().Should().Be("steer");
        messages.Should().HaveCount(4, "the agent's conversation is not changed");
    }

    [Fact]
    public void Mistral_AToolFollowedByTheModelsReply_GetsNoPlaceholder()
    {
        var messages = HookAfterToolBatch();
        messages[3] = new Message { Role = "assistant", Content = new List<ContentBlock> { new() { Type = "text", Text = "ok" } } };

        var payload = OpenAiStylePayload(new MistralProvider("key"), messages, streaming: false).GetProperty("messages");

        Roles(payload).Should().Equal("system", "user", "assistant", "tool", "assistant");
    }

    [Fact]
    public void Mistral_SeveralToolResults_PlaceholderOnlyAfterTheLast()
    {
        var messages = HookAfterToolBatch();
        messages[2] = new Message
        {
            Role = "user",
            Content = new List<object>
            {
                new { type = "tool_result", tool_use_id = "t1", content = "a" },
                new { type = "tool_result", tool_use_id = "t2", content = "b" }
            }
        };

        var payload = OpenAiStylePayload(new MistralProvider("key"), messages, streaming: false).GetProperty("messages");

        Roles(payload).Should().Equal("system", "user", "assistant", "tool", "tool", "assistant", "user");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OpenAiCompatible_PassesAUserAfterAToolMessageThrough(bool streaming)
    {
        var payload = OpenAiStylePayload(new VllmProvider(model: "m"), HookAfterToolBatch(), streaming).GetProperty("messages");

        Roles(payload).Should().Equal("system", "user", "assistant", "tool", "user");
    }

    [Fact]
    public void Gemini_MergesTheToolResultAndTheHooksMessageIntoOneUserContent()
    {
        var messages = HookAfterToolBatch();
        var contents = StaticPayload(typeof(GeminiProvider), messages, new List<Tool>(), "system").GetProperty("contents");

        Roles(contents).Should().Equal("user", "model", "user");
        var parts = contents[2].GetProperty("parts");
        parts.GetArrayLength().Should().Be(2);
        parts[0].TryGetProperty("functionResponse", out _).Should().BeTrue("the tool result stays first");
        parts[1].GetProperty("text").GetString().Should().Be("steer");
        messages.Should().HaveCount(4, "the agent's conversation is not changed");
    }

    [Fact]
    public void Gemini_AnAlternatingConversation_IsUnchanged()
    {
        var messages = HookAfterToolBatch();
        messages.RemoveAt(3);

        var contents = StaticPayload(typeof(GeminiProvider), messages, new List<Tool>(), "system").GetProperty("contents");

        Roles(contents).Should().Equal("user", "model", "user");
        contents[2].GetProperty("parts").GetArrayLength().Should().Be(1);
    }

    [Fact]
    public void Claude_PassesConsecutiveUserTurnsThrough()
    {
        var payload = StaticPayload(typeof(ClaudeProvider), HookAfterToolBatch(), new List<Tool>(), "system", "model");

        Roles(payload.GetProperty("messages")).Should().Equal("user", "assistant", "user", "user");
    }
}
