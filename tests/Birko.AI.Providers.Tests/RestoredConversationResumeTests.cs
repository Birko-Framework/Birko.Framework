using System.Net;
using System.Text.Json;
using Birko.AI.Agents;
using Birko.AI.Models;
using Birko.AI.Providers;
using Birko.AI.Tools;
using FluentAssertions;
using Xunit;

namespace Birko.AI.Providers.Tests;

/// <summary>
/// TASK-535 — a conversation saved with <see cref="ConversationCheckpoint"/> and restored resumes through
/// <c>Agent.ResumeAsync</c> (TASK-527), and each provider sends its tool_use / tool_result pair exactly as for a live
/// conversation.
/// </summary>
public class RestoredConversationResumeTests
{
    private sealed class CapturingHandler(string responseBody) : HttpMessageHandler
    {
        public string? Body;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Body = await request.Content!.ReadAsStringAsync(ct);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(responseBody) };
        }
    }

    private sealed class TestAgent(ILlmProvider provider) : Agent(provider, new AgentOptions { Verbose = false })
    {
        protected override string SystemPrompt => "system";
        protected override List<Tool> CreateTools() => [];
    }

    private sealed class Compatible(HttpMessageHandler h) : OpenAiCompatibleProviderBase("model", "http://localhost:8000", "key", h)
    {
        protected override string ProviderName => "Compatible";
    }

    private static List<Message> Restored()
    {
        var live = new List<Message>
        {
            new() { Role = "user", Content = "read a.txt" },
            new()
            {
                Role = "assistant",
                Content = new List<ContentBlock> { new() { Type = "tool_use", Id = "t1", Name = "read_file", Input = new() { ["path"] = "a.txt" } } },
            },
            new() { Role = "user", Content = new List<object> { new { type = "tool_result", tool_use_id = "t1", content = "file body" } } },
        };
        return ConversationCheckpoint.Deserialize(ConversationCheckpoint.Serialize(live));
    }

    [Fact]
    public async Task An_openai_compatible_provider_sends_the_restored_tool_call_and_result()
    {
        var handler = new CapturingHandler("{\"choices\":[{\"message\":{\"content\":\"ok\"}}]}");
        var conversation = await new TestAgent(new Compatible(handler)).ResumeAsync(Restored());

        conversation[^1].GetText().Should().Be("ok");
        var messages = JsonDocument.Parse(handler.Body!).RootElement.GetProperty("messages").EnumerateArray().ToList();
        messages[2].GetProperty("tool_calls")[0].GetProperty("id").GetString().Should().Be("t1");
        messages[2].GetProperty("tool_calls")[0].GetProperty("function").GetProperty("arguments").GetString().Should().Be("{\"path\":\"a.txt\"}");
        messages[3].GetProperty("role").GetString().Should().Be("tool");
        messages[3].GetProperty("tool_call_id").GetString().Should().Be("t1");
        messages[3].GetProperty("content").GetString().Should().Be("file body");
    }

    [Fact]
    public async Task Claude_sends_the_restored_tool_use_and_result()
    {
        var handler = new CapturingHandler("{\"stop_reason\":\"end_turn\",\"content\":[{\"type\":\"text\",\"text\":\"ok\"}]}");
        var conversation = await new TestAgent(new ClaudeProvider("key", "model", handler: handler)).ResumeAsync(Restored());

        conversation[^1].GetText().Should().Be("ok");
        var messages = JsonDocument.Parse(handler.Body!).RootElement.GetProperty("messages").EnumerateArray().ToList();
        var toolUse = messages[1].GetProperty("content")[0];
        toolUse.GetProperty("type").GetString().Should().Be("tool_use");
        toolUse.GetProperty("id").GetString().Should().Be("t1");
        toolUse.GetProperty("input").GetProperty("path").GetString().Should().Be("a.txt");
        var toolResult = messages[2].GetProperty("content")[0];
        toolResult.GetProperty("type").GetString().Should().Be("tool_result");
        toolResult.GetProperty("tool_use_id").GetString().Should().Be("t1");
        toolResult.GetProperty("content").GetString().Should().Be("file body");
    }
}
