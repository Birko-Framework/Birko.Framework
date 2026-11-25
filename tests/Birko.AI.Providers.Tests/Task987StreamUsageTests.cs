using System.Net;
using System.Reflection;
using System.Text.Json;
using Birko.AI.Models;
using Birko.AI.Providers;
using Birko.AI.Tools;
using FluentAssertions;
using Xunit;

namespace Birko.AI.Providers.Tests;

/// <summary>
/// TASK-987 (found by DraCode at TASK-984's human test): a streamed call through an OpenAI-compatible provider recorded
/// no token usage, because the request never asked for it (<c>stream_options.include_usage</c>), so the server sent no
/// usage chunk; cost reports and token rate limits missed every streamed reply. The parser already read a top-level
/// <c>usage</c> chunk; Groq puts it under <c>x_groq.usage</c>. OpenRouter deprecated the option (it always sends usage),
/// so it is not sent there.
/// </summary>
public class Task987StreamUsageTests
{
    private static readonly List<Message> Hi = [new() { Role = "user", Content = "hi" }];

    private static JsonElement StreamingPayload(OpenAiCompatibleProviderBase provider)
    {
        var method = typeof(OpenAiCompatibleProviderBase).GetMethod("BuildStreamingPayload", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var payload = method.Invoke(provider, new object[] { Hi, new List<Tool>(), "system" })!;
        return JsonSerializer.SerializeToElement(payload);
    }

    private sealed class Capturing(string body) : HttpMessageHandler
    {
        public string? RequestBody { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) };
        }
    }

    private sealed class Compatible(HttpMessageHandler h) : OpenAiCompatibleProviderBase("model", "http://localhost:8000", "key", h)
    {
        protected override string ProviderName => "Compatible";
    }

    private static string Sse(params string[] events) => string.Concat(events.Select(e => $"data: {e}\n\n")) + "data: [DONE]\n\n";

    private static async Task<LlmStreamingResponse> Drain(ILlmProvider provider)
    {
        var streaming = await provider.SendMessageStreamingAsync(Hi, new List<Tool>(), "sys");
        streaming.Error.Should().BeNull();
        await foreach (var _ in await streaming.GetStreamAsync()) { }
        return streaming;
    }

    [Theory]
    [InlineData("vllm")]
    [InlineData("groq")]
    [InlineData("deepseek")]
    public void A_streaming_request_asks_for_usage(string which)
    {
        OpenAiCompatibleProviderBase provider = which switch
        {
            "vllm" => new VllmProvider(model: "m"),
            "groq" => new GroqProvider("key"),
            _ => new DeepSeekProvider("key"),
        };

        var payload = StreamingPayload(provider);

        payload.GetProperty("stream").GetBoolean().Should().BeTrue();
        payload.GetProperty("stream_options").GetProperty("include_usage").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public void OpenRouter_does_not_send_the_deprecated_option()
    {
        var payload = StreamingPayload(new OpenRouterProvider("key"));

        payload.TryGetProperty("stream_options", out _).Should().BeFalse("OpenRouter deprecated it and always sends usage");
    }

    [Fact]
    public void A_non_streaming_request_does_not_carry_it()
    {
        var method = typeof(OpenAiCompatibleProviderBase).GetMethod("BuildPayload", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var payload = JsonSerializer.SerializeToElement(method.Invoke(new VllmProvider(model: "m"), new object[] { Hi, new List<Tool>(), "s" })!);

        payload.TryGetProperty("stream_options", out _).Should().BeFalse("DeepSeek answers 400 to stream_options without stream");
    }

    [Fact]
    public async Task OpenAI_streaming_asks_for_usage_and_reads_it()
    {
        var handler = new Capturing(Sse(
            """{"choices":[{"index":0,"delta":{"content":"Hello"}}],"usage":null}""",
            """{"choices":[{"index":0,"delta":{},"finish_reason":"stop"}],"usage":null}""",
            """{"model":"gpt-4o","choices":[],"usage":{"prompt_tokens":12,"completion_tokens":3,"total_tokens":15}}"""));

        var streaming = await Drain(new OpenAiProvider("key", handler: handler));

        JsonDocument.Parse(handler.RequestBody!).RootElement.GetProperty("stream_options").GetProperty("include_usage").GetBoolean().Should().BeTrue();
        streaming.Usage.Should().NotBeNull();
        streaming.Usage!.PromptTokens.Should().Be(12);
        streaming.Usage.CompletionTokens.Should().Be(3);
    }

    [Fact]
    public async Task Groqs_usage_under_x_groq_is_read()
    {
        var handler = new Capturing(Sse(
            """{"choices":[{"index":0,"delta":{"content":"Hi"}}]}""",
            """{"model":"llama","choices":[{"index":0,"delta":{},"finish_reason":"stop"}],"x_groq":{"id":"req_1","usage":{"prompt_tokens":20,"completion_tokens":5,"total_tokens":25}}}"""));

        var streaming = await Drain(new Compatible(handler));

        streaming.Usage.Should().NotBeNull("Groq reports streamed usage under x_groq.usage");
        streaming.Usage!.PromptTokens.Should().Be(20);
        streaming.Usage.CompletionTokens.Should().Be(5);
    }
}
