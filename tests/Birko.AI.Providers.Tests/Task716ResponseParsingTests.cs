using System.Net;
using System.Text.Json;
using Birko.AI.Models;
using Birko.AI.Providers;
using Birko.AI.Tools;
using Birko.Communication.OAuth;
using FluentAssertions;
using Xunit;

namespace Birko.AI.Providers.Tests;

/// <summary>
/// TASK-716. Measured before the fix, each against a canned body: Ollama's native reply sends tool-call
/// <c>arguments</c> as an object and the parser read a string, so every Ollama tool call failed (sync: an error reply;
/// streaming: the enumeration threw); Gemini let the last part decide the stop reason, so a function call followed by
/// text ended the turn without running it, and a blocked stream ended in an error with no message; Claude ignored a
/// mid-stream <c>error</c> event and reported the turn complete, and kept a tool call cut off by the token limit; the
/// OpenAI-compatible parser failed on <c>"error": null</c>, ran tool calls cut off by <c>finish_reason: length</c> and
/// never read <c>usage</c>; Copilot threw when sign-in failed instead of answering an error.
/// </summary>
public class Task716ResponseParsingTests
{
    private sealed class Canned(string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
    }

    private static readonly List<Message> Hi = [new() { Role = "user", Content = "hi" }];

    private static async Task<LlmResponse> Stream(ILlmProvider provider)
    {
        await using var streaming = await provider.SendMessageStreamingAsync(Hi, new List<Tool>(), "sys");
        streaming.Error.Should().BeNull();
        await foreach (var _ in await streaming.GetStreamAsync()) { }
        streaming.FinalResponse.Should().NotBeNull();
        return streaming.FinalResponse!;
    }

    private static string Sse(params string[] events) => string.Concat(events.Select(e => $"data: {e}\n\n"));

    // ── Ollama ──────────────────────────────────────────────────────────────────────────────────────────────

    private const string OllamaToolCall =
        """{"model":"llama3.2","message":{"role":"assistant","content":"","tool_calls":[{"function":{"name":"read_file","arguments":{"path":"a.txt","lines":{"from":1}}}}]},"done":true}""";

    [Fact]
    public async Task Ollama_reads_native_object_arguments()
    {
        var response = await new OllamaProvider("llama3.2", "http://ollama", new Canned(OllamaToolCall))
            .SendMessageAsync(Hi, new List<Tool>(), "sys");

        response.StopReason.Should().Be("tool_use", response.ErrorMessage);
        var call = response.Content!.Single(b => b.Type == "tool_use");
        call.Name.Should().Be("read_file");
        call.Input!["path"].ToString().Should().Be("a.txt");
        ((JsonElement)call.Input["lines"]).ValueKind.Should().Be(JsonValueKind.Object);
    }

    [Fact]
    public async Task Ollama_streaming_reads_native_object_arguments()
    {
        var final = await Stream(new OllamaProvider("llama3.2", "http://ollama", new Canned(OllamaToolCall + "\n")));

        final.StopReason.Should().Be("tool_use");
        final.Content!.Single(b => b.Type == "tool_use").Input!["path"].ToString().Should().Be("a.txt");
    }

    [Fact]
    public async Task Ollama_still_reads_string_arguments()
    {
        var body = """{"message":{"role":"assistant","content":"","tool_calls":[{"function":{"name":"read_file","arguments":"{\"path\":\"a.txt\"}"}}]},"done":true}""";

        var response = await new OllamaProvider("llama3.2", "http://ollama", new Canned(body)).SendMessageAsync(Hi, new List<Tool>(), "sys");

        response.Content!.Single(b => b.Type == "tool_use").Input!["path"].ToString().Should().Be("a.txt");
    }

    // ── Gemini ──────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Gemini_function_call_followed_by_text_is_a_tool_turn()
    {
        var body = """{"candidates":[{"content":{"parts":[{"functionCall":{"name":"read_file","args":{"path":"a.txt"}}},{"text":"Reading it now."}]},"finishReason":"STOP"}]}""";

        var response = await new GeminiProvider("key", "gemini", "http://gemini/", new Canned(body)).SendMessageAsync(Hi, new List<Tool>(), "sys");

        response.StopReason.Should().Be("tool_use");
        response.Content!.Should().Contain(b => b.Type == "tool_use" && b.Name == "read_file");
    }

    [Fact]
    public async Task Gemini_blocked_stream_says_why()
    {
        var final = await Stream(new GeminiProvider("key", "gemini", "http://gemini/",
            new Canned(Sse("""{"candidates":[{"finishReason":"SAFETY"}]}"""))));

        final.StopReason.Should().Be("error");
        final.ErrorMessage.Should().Contain("SAFETY");
    }

    [Fact]
    public async Task Gemini_streamed_function_call_keeps_nested_arguments()
    {
        var final = await Stream(new GeminiProvider("key", "gemini", "http://gemini/",
            new Canned(Sse("""{"candidates":[{"content":{"parts":[{"functionCall":{"name":"search","args":{"q":"x","opts":{"limit":5}}}}]},"finishReason":"STOP"}]}"""))));

        var input = final.Content!.Single(b => b.Type == "tool_use").Input!;
        input["q"].ToString().Should().Be("x");
        input["opts"].Should().BeOfType<JsonElement>().Which.ValueKind.Should().Be(JsonValueKind.Object);
    }

    // ── Claude ──────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Claude_mid_stream_error_event_fails_the_turn()
    {
        var final = await Stream(new ClaudeProvider("key", "claude", "http://claude", new Canned(Sse(
            """{"type":"message_start","message":{"usage":{"input_tokens":10}}}""",
            """{"type":"content_block_start","index":0,"content_block":{"type":"text","text":""}}""",
            """{"type":"content_block_delta","index":0,"delta":{"type":"text_delta","text":"Half an ans"}}""",
            """{"type":"error","error":{"type":"overloaded_error","message":"Overloaded"}}"""))));

        final.StopReason.Should().Be("error");
        final.ErrorMessage.Should().Contain("Overloaded");
        final.Failure!.Kind.Should().Be(LlmFailureKind.ServerError);
    }

    [Fact]
    public async Task Claude_tool_call_cut_off_by_the_token_limit_is_not_returned()
    {
        var final = await Stream(new ClaudeProvider("key", "claude", "http://claude", new Canned(Sse(
            """{"type":"message_start","message":{"usage":{"input_tokens":10}}}""",
            """{"type":"content_block_start","index":0,"content_block":{"type":"tool_use","id":"tu_1","name":"write_file","input":{}}}""",
            """{"type":"content_block_delta","index":0,"delta":{"type":"input_json_delta","partial_json":"{\"path\":\"a.txt\",\"content\":\"abc"}}""",
            """{"type":"content_block_stop","index":0}""",
            """{"type":"message_delta","delta":{"stop_reason":"max_tokens"},"usage":{"output_tokens":8192}}""",
            """{"type":"message_stop"}"""))));

        final.StopReason.Should().Be("max_tokens");
        final.Content!.Should().NotContain(b => b.Type == "tool_use");
    }

    // ── OpenAI-compatible ───────────────────────────────────────────────────────────────────────────────────

    private sealed class Compat(HttpMessageHandler handler) : OpenAiCompatibleProviderBase("m", "http://local", null, handler)
    {
        protected override string ProviderName => "Local";
    }

    private static Task<LlmResponse> Compatible(string body) => new Compat(new Canned(body)).SendMessageAsync(Hi, new List<Tool>(), "sys");

    [Fact]
    public async Task Compatible_error_null_is_no_error()
    {
        var response = await Compatible("""{"error":null,"choices":[{"message":{"role":"assistant","content":"hello"},"finish_reason":"stop"}]}""");

        response.StopReason.Should().Be("end_turn", response.ErrorMessage);
        response.Content!.Single().Text.Should().Be("hello");
    }

    [Fact]
    public async Task Compatible_reads_usage()
    {
        var response = await Compatible("""{"choices":[{"message":{"role":"assistant","content":"hello"},"finish_reason":"stop"}],"usage":{"prompt_tokens":12,"completion_tokens":3}}""");

        response.Usage.Should().NotBeNull();
        response.Usage!.PromptTokens.Should().Be(12);
        response.Usage.CompletionTokens.Should().Be(3);
    }

    [Fact]
    public async Task Compatible_tool_call_cut_off_by_length_is_not_run()
    {
        var response = await Compatible("""{"choices":[{"message":{"role":"assistant","content":null,"tool_calls":[{"id":"c1","type":"function","function":{"name":"write_file","arguments":"{\"path\":\"a"}}]},"finish_reason":"length"}]}""");

        response.StopReason.Should().Be("max_tokens");
        response.Content!.Should().NotContain(b => b.Type == "tool_use");
    }

    [Fact]
    public async Task Compatible_tool_call_without_an_id_still_gets_one()
    {
        var response = await Compatible("""{"choices":[{"message":{"role":"assistant","tool_calls":[{"function":{"name":"read_file","arguments":"{\"path\":\"a.txt\"}"}}]},"finish_reason":"tool_calls"}]}""");

        response.StopReason.Should().Be("tool_use");
        response.Content!.Single(b => b.Type == "tool_use").Id.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Compatible_error_object_is_reported_with_the_provider_name()
    {
        var response = await Compatible("""{"error":{"message":"model not loaded"}}""");

        response.StopReason.Should().Be("error");
        response.ErrorMessage.Should().Contain("Local").And.Contain("model not loaded");
    }

    // ── GitHub Copilot ──────────────────────────────────────────────────────────────────────────────────────

    private sealed class FailingSignIn : IOAuthClient
    {
        public Task<OAuthToken> GetTokenAsync(CancellationToken ct = default) => throw new HttpRequestException("device code expired");
        public Task<OAuthToken> RefreshTokenAsync(CancellationToken ct = default) => throw new HttpRequestException("device code expired");
        public Task<OAuthToken> ExchangeCodeAsync(string code, string? codeVerifier = null, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<OAuthToken> PollDeviceTokenAsync(string deviceCode, int? intervalSeconds = null, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<DeviceAuthorizationResponse> RequestDeviceAuthorizationAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public string BuildAuthorizationUrl(string state, PkceChallenge? pkceChallenge = null) => string.Empty;
        public void ClearTokenCache() { }
        public void Dispose() { }
    }

    [Fact]
    public async Task Copilot_failed_sign_in_is_an_error_reply()
    {
        var response = await new GitHubCopilotProvider(new FailingSignIn(), handler: new Canned("{}")).SendMessageAsync(Hi, new List<Tool>(), "sys");

        response.StopReason.Should().Be("error");
        response.ErrorMessage.Should().Contain("device code expired");
    }

    [Fact]
    public async Task Copilot_failed_sign_in_is_a_streaming_error()
    {
        await using var streaming = await new GitHubCopilotProvider(new FailingSignIn(), handler: new Canned("{}"))
            .SendMessageStreamingAsync(Hi, new List<Tool>(), "sys");

        streaming.Error.Should().Contain("device code expired");
        streaming.FinalResponse!.StopReason.Should().Be("error");
    }
}
