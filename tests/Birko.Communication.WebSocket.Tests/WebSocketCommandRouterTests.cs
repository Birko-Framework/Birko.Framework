using System;
using System.IO;
using System.IO.Pipelines;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Birko.Communication.WebSocket.Messaging;
using FluentAssertions;
using Xunit;
using SysWebSocket = System.Net.WebSockets.WebSocket;

namespace Birko.Communication.WebSocket.Tests;

/// <summary>
/// TASK-539: <see cref="WebSocketCommandRouter"/>, the server half of birko-web-core's <c>WsClient.request</c>
/// (TASK-523). Requests are written in the shape WsClient sends (<c>{ type, id, data }</c> with a <c>req_…</c> string
/// id), and replies are checked for what its <c>_settleReply</c> reads: the same id, <c>type</c> "response" with
/// <c>data</c>, or "error" with a string <c>error</c>.
/// </summary>
public class WebSocketCommandRouterTests
{
    private sealed record Range(int From, int To);

    private static WebSocketCommandRouter Router(WebSocketCommandRouterOptions? options = null) =>
        new WebSocketCommandRouter(options)
            .Map("get_stats", (_, _) => Task.FromResult<object?>(new { Projects = 3, Agents = 7 }))
            .Map<Range>("sum", (range, _, _) => Task.FromResult<object?>(range is null ? 0 : range.From + range.To))
            .Map("explode", (_, _) => throw new InvalidOperationException("C:\\secret\\path.db is locked"))
            .Map("refuse", (_, _) => throw new WebSocketCommandException("Project not found."));

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement.Clone();

    [Fact]
    public async Task A_request_gets_a_response_with_its_id_and_data()
    {
        var reply = Parse(await Router().HandleAsync("""{"type":"get_stats","id":"req_lx3_1","data":null}"""));

        reply.GetProperty("id").GetString().Should().Be("req_lx3_1");
        reply.GetProperty("type").GetString().Should().Be("response");
        reply.GetProperty("data").GetProperty("projects").GetInt32().Should().Be(3, "payloads are camelCase");
        reply.TryGetProperty("timestamp", out _).Should().BeTrue();
    }

    [Fact]
    public async Task Typed_data_is_deserialized()
    {
        var reply = Parse(await Router().HandleAsync("""{"type":"sum","id":"r1","data":{"from":2,"to":5}}"""));

        reply.GetProperty("data").GetInt32().Should().Be(7);
    }

    [Fact]
    public async Task Command_names_match_regardless_of_case_by_default()
    {
        var reply = Parse(await Router().HandleAsync("""{"type":"GET_STATS","id":"r1"}"""));

        reply.GetProperty("type").GetString().Should().Be("response");
    }

    [Theory]
    [InlineData("""{"type":"nope","id":"r9"}""", "Unknown command: nope")]
    [InlineData("""{"type":"sum","id":"r9","data":{"from":"two"}}""", "Invalid data for command 'sum'")]
    [InlineData("""{"type":"refuse","id":"r9"}""", "Project not found.")]
    [InlineData("""{"type":"explode","id":"r9"}""", "Command 'explode' failed.")]
    [InlineData("""{"id":"r9","data":{}}""", "The message has no 'type'.")]
    public async Task Every_error_reply_carries_the_request_id(string request, string error)
    {
        var reply = Parse(await Router().HandleAsync(request));

        reply.GetProperty("id").GetString().Should().Be("r9", "a correlating client fails at once instead of timing out");
        reply.GetProperty("type").GetString().Should().Be("error");
        reply.GetProperty("error").GetString().Should().StartWith(error);
    }

    [Fact]
    public async Task An_unexpected_exception_message_is_not_sent_unless_allowed()
    {
        var hidden = Parse(await Router().HandleAsync("""{"type":"explode","id":"r1"}"""));
        hidden.GetProperty("error").GetString().Should().NotContain("secret");

        var exposed = Parse(await Router(new WebSocketCommandRouterOptions { ExposeExceptionMessages = true })
            .HandleAsync("""{"type":"explode","id":"r1"}"""));
        exposed.GetProperty("error").GetString().Should().Contain("secret");
    }

    [Theory]
    [InlineData("{not json")]
    [InlineData("[1,2]")]
    [InlineData("")]
    public async Task Malformed_requests_get_an_error_reply_without_an_id(string request)
    {
        var reply = Parse(await Router().HandleAsync(request));

        reply.TryGetProperty("id", out _).Should().BeFalse("there is no id to echo");
        reply.GetProperty("type").GetString().Should().Be("error");
    }

    [Fact]
    public async Task A_request_without_an_id_is_answered_without_one()
    {
        var reply = Parse(await Router().HandleAsync("""{"type":"get_stats"}"""));

        reply.TryGetProperty("id", out _).Should().BeFalse();
        reply.GetProperty("type").GetString().Should().Be("response");
    }

    [Fact]
    public async Task A_numeric_id_is_echoed_as_sent()
    {
        var reply = Parse(await Router().HandleAsync("""{"type":"get_stats","id":42}"""));

        reply.GetProperty("id").GetInt32().Should().Be(42);
    }

    [Fact]
    public async Task The_command_and_id_fields_are_configurable_for_other_clients()
    {
        var router = Router(new WebSocketCommandRouterOptions { CommandField = "command", IdField = "requestId" });

        var reply = Parse(await router.HandleAsync("""{"command":"get_stats","requestId":"x1"}"""));

        reply.GetProperty("requestId").GetString().Should().Be("x1");
        reply.GetProperty("type").GetString().Should().Be("response");
    }

    [Fact]
    public void A_command_is_mapped_once()
    {
        var act = () => Router().Map("get_stats", (_, _) => Task.FromResult<object?>(null));

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public async Task Over_a_socket_every_request_is_answered_and_a_slow_one_does_not_hold_up_the_next()
    {
        var gate = new TaskCompletionSource();
        var router = Router().Map("slow", async (_, _) => { await gate.Task; return "late"; });
        var (server, client) = Pair();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var run = router.RunAsync(server, cancellationToken: cts.Token);

        await Send(client, """{"type":"slow","id":"a"}""");
        await Send(client, """{"type":"get_stats","id":"b"}""");
        Parse(await Receive(client, cts.Token)).GetProperty("id").GetString().Should().Be("b", "the slow command runs alongside");

        gate.SetResult();
        var late = Parse(await Receive(client, cts.Token));
        late.GetProperty("id").GetString().Should().Be("a");
        late.GetProperty("data").GetString().Should().Be("late");

        await Send(client, new byte[] { 1, 2 }, WebSocketMessageType.Binary);
        Parse(await Receive(client, cts.Token)).GetProperty("type").GetString().Should().Be("error");

        await client.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, cts.Token);
        await run.WaitAsync(cts.Token);
    }

    private static Task Send(SysWebSocket socket, string text) =>
        Send(socket, Encoding.UTF8.GetBytes(text), WebSocketMessageType.Text);

    private static Task Send(SysWebSocket socket, byte[] data, WebSocketMessageType type) =>
        socket.SendAsync(data, type, true, CancellationToken.None);

    private static async Task<string> Receive(SysWebSocket socket, CancellationToken ct)
    {
        var message = await socket.ReceiveMessageAsync(cancellationToken: ct);
        message.IsMessage.Should().BeTrue();
        return message.GetText();
    }

    private static (SysWebSocket server, SysWebSocket client) Pair()
    {
        var toServer = new Pipe();
        var toClient = new Pipe();
        var server = SysWebSocket.CreateFromStream(new DuplexStream(toServer.Reader.AsStream(), toClient.Writer.AsStream()), true, null, TimeSpan.Zero);
        var client = SysWebSocket.CreateFromStream(new DuplexStream(toClient.Reader.AsStream(), toServer.Writer.AsStream()), false, null, TimeSpan.Zero);
        return (server, client);
    }

    private sealed class DuplexStream(Stream read, Stream write) : Stream
    {
        public override bool CanRead => true;
        public override bool CanWrite => true;
        public override bool CanSeek => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() => write.Flush();
        public override Task FlushAsync(CancellationToken ct) => write.FlushAsync(ct);
        public override int Read(byte[] buffer, int offset, int count) => read.Read(buffer, offset, count);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default) => read.ReadAsync(buffer, ct);
        public override void Write(byte[] buffer, int offset, int count) => write.Write(buffer, offset, count);
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default) => write.WriteAsync(buffer, ct);
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
