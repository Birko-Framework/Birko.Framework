using System.Net;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Birko.AI.Models;
using Birko.AI.Providers;
using Birko.AI.Tools;
using FluentAssertions;
using Xunit;

namespace Birko.AI.Providers.Tests;

/// <summary>
/// TASK-160 — the medium spec-harvest findings in the OpenAI-style parsers and message builder of
/// <see cref="LlmProviderBase"/>: SH-M157..M166.
/// </summary>
public class Task160ParserAndMessageTests
{
    private sealed class Probe : LlmProviderBase
    {
        public override string Name => "probe";
        protected override bool IsConfigured() => true;
        public override Task<LlmResponse> SendMessageAsync(List<Message> messages, List<Tool> tools, string systemPrompt, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public static LlmResponse Parse(string json) => ParseOpenAiStyleResponse(json);
        public static List<object> Build(IEnumerable<Message> messages) => BuildOpenAiStyleMessages(messages, "system");

        public static async Task<(List<string> Text, LlmStreamingResponse Response)> Stream(params string[] chunks)
        {
            var response = new LlmStreamingResponse { GetStreamAsync = () => throw new NotSupportedException() };
            var text = new List<string>();
            await foreach (var t in ParseOpenAiStreamChunksWithToolCapture(Chunks(chunks), response)) text.Add(t);
            return (text, response);
        }

        public static async Task<List<string>> StreamPlain(params string[] chunks)
        {
            var text = new List<string>();
            await foreach (var t in ParseOpenAiStreamChunks(Chunks(chunks))) text.Add(t);
            return text;
        }

        public Task<HttpResponseMessage?> Connect(HttpClient client, Func<HttpRequestMessage> factory)
            => SendStreamingWithRetryAsync(client, factory, Name);
    }

    private static async IAsyncEnumerable<string> Chunks(string[] chunks, [EnumeratorCancellation] CancellationToken ct = default)
    {
        foreach (var c in chunks)
        {
            await Task.Yield();
            yield return c;
        }
    }

    private const string Text = "{\"message\":{\"content\":\"hello\"}";

    // ── SH-M157..M160: the sync parser ──

    [Fact]
    public void SH_M157_a_null_error_property_does_not_discard_a_valid_completion()
    {
        var response = Probe.Parse("{\"error\":null,\"choices\":[" + Text + "}]}");
        response.StopReason.Should().Be("end_turn");
        response.Content!.Single().Text.Should().Be("hello");
    }

    [Fact]
    public void SH_M158_null_tool_calls_on_a_text_turn_is_a_text_turn()
    {
        var response = Probe.Parse("{\"choices\":[{\"message\":{\"content\":\"hello\",\"tool_calls\":null}}]}");
        response.StopReason.Should().Be("end_turn");
        response.Content!.Single().Text.Should().Be("hello");
    }

    [Theory]
    [InlineData("null", 0)]
    [InlineData("12.0", 12)]
    [InlineData("\"7\"", 0)]
    public void SH_M159_an_odd_token_count_does_not_discard_the_payload(string count, int expected)
    {
        var response = Probe.Parse("{\"choices\":[" + Text + "}],\"usage\":{\"prompt_tokens\":" + count + ",\"completion_tokens\":3}}");
        response.StopReason.Should().Be("end_turn");
        response.Usage!.PromptTokens.Should().Be(expected);
        response.Usage.CompletionTokens.Should().Be(3);
    }

    [Theory]
    [InlineData("length", "max_tokens")]
    [InlineData("content_filter", "content_filter")]
    [InlineData("stop", "end_turn")]
    public void SH_M160_the_sync_parser_reports_why_the_completion_stopped(string finishReason, string stopReason)
        => Probe.Parse("{\"choices\":[{\"message\":{\"content\":\"hel\"},\"finish_reason\":\"" + finishReason + "\"}]}")
            .StopReason.Should().Be(stopReason);

    [Fact]
    public void SH_M160_a_tool_call_cut_off_by_the_length_limit_is_not_run()
    {
        var response = Probe.Parse("{\"choices\":[{\"message\":{\"content\":null,\"tool_calls\":[{\"id\":\"c1\",\"function\":{\"name\":\"list_files\",\"arguments\":\"{\\\"dir\"}}]},\"finish_reason\":\"length\"}]}");
        response.StopReason.Should().Be("max_tokens");
        response.Content.Should().NotContain(b => b.Type == "tool_use");
    }

    // ── SH-M161..M163: the streaming parsers ──

    private static string Delta(string delta, string? finish = null)
        => "{\"choices\":[{\"delta\":" + delta + (finish == null ? "" : ",\"finish_reason\":\"" + finish + "\"") + "}]}";

    [Fact]
    public async Task SH_M161_a_stream_cut_off_mid_tool_call_does_not_run_the_truncated_call()
    {
        var (_, response) = await Probe.Stream(
            Delta("{\"tool_calls\":[{\"index\":0,\"id\":\"c1\",\"function\":{\"name\":\"list_files\",\"arguments\":\"{\\\"dir\"}}]}"),
            Delta("{}", "length"));

        response.FinalResponse!.StopReason.Should().Be("max_tokens");
        response.FinalResponse.Content.Should().NotContain(b => b.Type == "tool_use");
    }

    [Fact]
    public async Task SH_M162_an_odd_usage_chunk_does_not_throw_mid_stream()
    {
        var (text, response) = await Probe.Stream(
            Delta("{\"content\":\"hi\"}"),
            "{\"choices\":[],\"usage\":{\"prompt_tokens\":null,\"completion_tokens\":2.0}}");

        text.Should().Equal("hi");
        response.FinalResponse!.Usage!.CompletionTokens.Should().Be(2);
        response.FinalResponse.Usage.PromptTokens.Should().Be(0);
    }

    [Fact]
    public async Task SH_M163_a_chunk_without_a_delta_does_not_throw_mid_stream()
    {
        var text = await Probe.StreamPlain(Delta("{\"content\":\"a\"}"), "{\"choices\":[{\"finish_reason\":\"stop\"}]}", Delta("{\"content\":\"b\"}"));
        text.Should().Equal("a", "b");
    }

    [Fact]
    public async Task A_null_choices_or_tool_calls_value_in_a_stream_is_skipped()
    {
        var (text, response) = await Probe.Stream("{\"choices\":null}", Delta("{\"content\":\"x\",\"tool_calls\":null}", "stop"));
        text.Should().Equal("x");
        response.FinalResponse!.StopReason.Should().Be("end_turn");
    }

    // ── SH-M164: the streaming request is disposed ──

    private sealed class TrackingContent(string body) : StringContent(body)
    {
        public bool Disposed;
        protected override void Dispose(bool disposing) { Disposed = true; base.Dispose(disposing); }
    }

    private sealed class Answer(HttpStatusCode code) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
            => Task.FromResult(new HttpResponseMessage(code) { Content = new StringContent("{}") });
    }

    [Theory]
    [InlineData(HttpStatusCode.OK)]
    [InlineData(HttpStatusCode.BadRequest)]
    public async Task SH_M164_every_streaming_request_is_disposed(HttpStatusCode code)
    {
        var contents = new List<TrackingContent>();
        using var client = new HttpClient(new Answer(code));

        using var response = await new Probe().Connect(client, () =>
        {
            var content = new TrackingContent("{\"big\":\"prompt\"}");
            contents.Add(content);
            return new HttpRequestMessage(HttpMethod.Post, "http://localhost/x") { Content = content };
        });

        contents.Should().NotBeEmpty().And.OnlyContain(c => c.Disposed);
    }

    // ── SH-M165, SH-M166: the message builder ──

    [Fact]
    public void SH_M165_every_text_block_of_an_assistant_tool_turn_is_kept()
    {
        var built = Probe.Build([new Message
        {
            Role = "assistant",
            Content = new List<ContentBlock>
            {
                new() { Type = "text", Text = "plan" },
                new() { Type = "text", Text = "note" },
                new() { Type = "tool_use", Id = "c1", Name = "t", Input = new() },
            },
        }]);

        JsonDocument.Parse(JsonSerializer.Serialize(built[1])).RootElement.GetProperty("content").GetString().Should().Be("plan\nnote");
    }

    [Fact]
    public void SH_M166_tool_results_given_as_dictionaries_are_sent()
    {
        var built = Probe.Build([new Message
        {
            Role = "user",
            Content = new List<object> { new Dictionary<string, object?> { ["type"] = "tool_result", ["tool_use_id"] = "c1", ["content"] = "ok" } },
        }]);

        var tool = JsonDocument.Parse(JsonSerializer.Serialize(built[1])).RootElement;
        tool.GetProperty("role").GetString().Should().Be("tool");
        tool.GetProperty("tool_call_id").GetString().Should().Be("c1");
        tool.GetProperty("content").GetString().Should().Be("ok");
    }

    [Fact]
    public void SH_M166_a_typed_item_list_that_carries_no_tool_result_is_refused_not_dropped()
    {
        var act = () => Probe.Build([new Message { Role = "user", Content = new List<object> { new { type = "image", url = "x" } } }]);
        act.Should().Throw<NotSupportedException>();
    }
}
