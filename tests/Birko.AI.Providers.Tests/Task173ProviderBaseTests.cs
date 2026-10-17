using System.Net;
using System.Text;
using System.Text.Json;
using Birko.AI.Models;
using Birko.AI.Providers;
using Birko.AI.Tools;
using FluentAssertions;
using Xunit;

namespace Birko.AI.Providers.Tests;

/// <summary>TASK-173 — the low findings in <see cref="LlmProviderBase"/>: SH-L146..L148, L150, L151, L153, L154.</summary>
public class Task173ProviderBaseTests
{
    private sealed class Probe : LlmProviderBase
    {
        public Probe(int maxRetries) => RetryPolicy = new Birko.RetryPolicy { MaxRetries = maxRetries, BaseDelay = TimeSpan.Zero };
        public List<string> Messages { get; } = [];
        public override string Name => "probe";
        protected override bool IsConfigured() => true;
        public override Task<LlmResponse> SendMessageAsync(List<Message> messages, List<Tool> tools, string systemPrompt, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<(HttpResponseMessage? Response, string? Body)> Send(HttpClient client)
        {
            MessageCallback = (type, text) => Messages.Add($"{type}: {text}");
            return SendWithRetryAsync(client, () => new HttpRequestMessage(HttpMethod.Post, "http://localhost/x"), Name);
        }

        public async Task<(HttpResponseMessage? Response, LlmFailure? Failure)> Connect(HttpClient client)
        {
            MessageCallback = (type, text) => Messages.Add($"{type}: {text}");
            var c = await ConnectStreamingAsync(client, () => new HttpRequestMessage(HttpMethod.Post, "http://localhost/x"), Name);
            return (c.Response, c.Failure);
        }

        public static async Task<List<string>> Sse(string body)
        {
            var list = new List<string>();
            await foreach (var chunk in ParseSseStream(new MemoryStream(Encoding.UTF8.GetBytes(body)))) list.Add(chunk);
            return list;
        }

        public static List<object> Build(IEnumerable<Message> messages) => BuildOpenAiStyleMessages(messages, "system");
    }

    private sealed class Counting(HttpStatusCode code, string body) : HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(new HttpResponseMessage(code) { Content = new StringContent(body) });
        }
    }

    [Fact]
    public async Task SH_L146_a_failed_streaming_request_reports_the_providers_explanation()
    {
        var probe = new Probe(0);
        await probe.Connect(new HttpClient(new Counting(HttpStatusCode.BadRequest, "{\"error\":{\"message\":\"model gpt-x does not exist\"}}")));
        probe.Messages.Should().Contain(m => m.Contains("model gpt-x does not exist"));
    }

    [Fact]
    public async Task SH_L148_a_negative_retry_count_still_sends_the_request_once()
    {
        var handler = new Counting(HttpStatusCode.OK, "{}");
        var (response, body) = await new Probe(-1).Send(new HttpClient(handler));

        handler.Calls.Should().Be(1);
        response.Should().NotBeNull();
        body.Should().Be("{}");
    }

    [Fact]
    public async Task SH_L148_a_negative_retry_count_still_opens_a_stream_once()
    {
        var handler = new Counting(HttpStatusCode.OK, "data: {}\n\n");
        var connection = await new Probe(-1).Connect(new HttpClient(handler));
        handler.Calls.Should().Be(1);
        connection.Response.Should().NotBeNull();
        connection.Response!.Dispose();
    }

    [Fact]
    public async Task SH_L150_a_data_line_without_the_optional_space_is_read()
        => (await Probe.Sse("data:{\"a\":1}\n\ndata: {\"b\":2}\n\n")).Should().Equal("{\"a\":1}", "{\"b\":2}");

    [Fact]
    public async Task SH_L151_a_done_frame_ends_the_stream_and_multi_line_data_joins_with_newlines()
    {
        var chunks = await Probe.Sse("data: {\"a\":\ndata: 1}\n\ndata: [DONE]\n\ndata: {\"late\":true}\n\n");
        chunks.Should().Equal("{\"a\":\n1}");
    }

    [Fact]
    public async Task SH_L151_an_unterminated_last_frame_is_still_yielded_without_a_trailing_newline()
        => (await Probe.Sse("data: {\"a\":1}")).Should().Equal("{\"a\":1}");

    [Fact]
    public void SH_L153_block_types_are_matched_exactly_as_the_agent_matches_them()
    {
        var built = Probe.Build([new Message
        {
            Role = "assistant",
            Content = new List<ContentBlock> { new() { Type = "Tool_Use", Id = "c1", Name = "t", Input = new() } },
        }]);

        JsonSerializer.Serialize(built[1]).Should().NotContain("tool_calls", "the agent would never execute a 'Tool_Use' block");
    }

    [Fact]
    public void SH_L154_a_message_without_a_role_is_refused_with_its_position()
    {
        var act = () => Probe.Build([new Message { Role = "user", Content = "a" }, new Message { Content = "b" }]);
        act.Should().Throw<InvalidOperationException>().WithMessage("*1*role*");
    }
}
