using System;
using System.Collections.Concurrent;
using System.IO;
using System.IO.Pipelines;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Birko.Communication.WebSocket.Messaging;
using Birko.Communication.WebSocket.Servers;
using FluentAssertions;
using Xunit;
using SysWebSocket = System.Net.WebSockets.WebSocket;

namespace Birko.Communication.WebSocket.Tests;

/// <summary>
/// TASK-537. <c>WebSocketServer.ReceiveLoopAsync</c> reassembled a message across frames into a <c>MemoryStream</c> with
/// no limit, so a client that never sent <c>EndOfMessage</c> grew server memory without bound. Receive now goes through
/// <see cref="WebSocketMessageExtensions.ReceiveMessageAsync"/>, which caps the message and closes the socket with
/// <see cref="WebSocketCloseStatus.MessageTooBig"/>. The same helper is what ASP.NET Core handlers use instead of a
/// one-<c>ReceiveAsync</c> loop that parses a fragment.
/// </summary>
public class WebSocketMessageReceiveTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    /// <summary>A connected server/client pair of managed WebSockets over in-memory pipes.</summary>
    private static (SysWebSocket server, SysWebSocket client) Pair()
    {
        var toServer = new Pipe();
        var toClient = new Pipe();
        var server = SysWebSocket.CreateFromStream(new DuplexStream(toServer.Reader.AsStream(), toClient.Writer.AsStream()), true, null, TimeSpan.Zero);
        var client = SysWebSocket.CreateFromStream(new DuplexStream(toClient.Reader.AsStream(), toServer.Writer.AsStream()), false, null, TimeSpan.Zero);
        return (server, client);
    }

    private static async Task SendInFramesAsync(SysWebSocket socket, byte[] data, int frameSize, WebSocketMessageType type, bool endMessage = true)
    {
        for (var offset = 0; offset < data.Length; offset += frameSize)
        {
            var count = Math.Min(frameSize, data.Length - offset);
            var last = endMessage && offset + count >= data.Length;
            await socket.SendAsync(new ArraySegment<byte>(data, offset, count), type, last, CancellationToken.None);
        }
    }

    [Fact]
    public async Task FragmentedBinaryMessage_IsReassembledWhole()
    {
        var (server, client) = Pair();
        var data = Enumerable.Range(0, 100_000).Select(i => (byte)(i % 251)).ToArray();

        var receive = server.ReceiveMessageAsync(maxMessageBytes: 1_000_000);
        await SendInFramesAsync(client, data, frameSize: 7_000, WebSocketMessageType.Binary);
        var message = await receive.WaitAsync(Timeout);

        message.Outcome.Should().Be(WebSocketReceiveOutcome.Message);
        message.MessageType.Should().Be(WebSocketMessageType.Binary);
        message.Data.Should().Equal(data);
    }

    [Fact]
    public async Task FragmentedTextMessage_DecodesWhole()
    {
        var (server, client) = Pair();
        var text = string.Concat(Enumerable.Repeat("{\"k\":\"čšž\"},", 5_000));

        var receive = server.ReceiveMessageAsync();
        await SendInFramesAsync(client, Encoding.UTF8.GetBytes(text), frameSize: 1_001, WebSocketMessageType.Text);
        var message = await receive.WaitAsync(Timeout);

        message.MessageType.Should().Be(WebSocketMessageType.Text);
        message.GetText().Should().Be(text, "a frame boundary inside a multi-byte character must not corrupt the text");
    }

    [Fact]
    public async Task MessageExactlyAtCap_IsAccepted()
    {
        var (server, client) = Pair();
        var data = new byte[4_096];

        var receive = server.ReceiveMessageAsync(maxMessageBytes: 4_096);
        await SendInFramesAsync(client, data, frameSize: 1_000, WebSocketMessageType.Binary);

        (await receive.WaitAsync(Timeout)).Outcome.Should().Be(WebSocketReceiveOutcome.Message);
    }

    [Fact]
    public async Task MessageOneByteOverCap_ClosesWithMessageTooBig_AndReturnsNoData()
    {
        var (server, client) = Pair();

        var receive = server.ReceiveMessageAsync(maxMessageBytes: 4_096);
        await SendInFramesAsync(client, new byte[4_097], frameSize: 1_000, WebSocketMessageType.Binary);
        var message = await receive.WaitAsync(Timeout);

        message.Outcome.Should().Be(WebSocketReceiveOutcome.TooBig);
        message.Data.Should().BeEmpty("a truncated message is never handed to the caller");

        var clientSees = await client.ReceiveAsync(new byte[64], CancellationToken.None).WaitAsync(Timeout);
        clientSees.MessageType.Should().Be(WebSocketMessageType.Close);
        client.CloseStatus.Should().Be(WebSocketCloseStatus.MessageTooBig);
    }

    [Fact]
    public async Task PeerClose_IsReturnedAsClosed_NotThrown()
    {
        var (server, client) = Pair();

        var receive = server.ReceiveMessageAsync();
        await client.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None);
        var message = await receive.WaitAsync(Timeout);

        message.Outcome.Should().Be(WebSocketReceiveOutcome.Closed);
        message.CloseStatus.Should().Be(WebSocketCloseStatus.NormalClosure);
        message.CloseDescription.Should().Be("bye");
        server.State.Should().Be(WebSocketState.Closed, "the close was acknowledged");
    }

    [Fact]
    public async Task NonPositiveCap_Throws()
    {
        var (server, _) = Pair();

        var act = () => server.ReceiveMessageAsync(maxMessageBytes: 0);

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    /// <summary>
    /// Contract pin, not a fix. The docs for <see cref="SysWebSocket"/> allow one outstanding send; TASK-537 was filed
    /// on that and was going to add a per-client send gate to <c>BroadcastAsync</c>. Measured on .NET 10.0.12, the
    /// <c>ManagedWebSocket</c> that Kestrel and HttpListener hand out queues concurrent sends behind its own mutex —
    /// 3 runs × 200 concurrent 100 KB sends, 0 threw, 0 corrupt — so the gate was not built. If a runtime stops
    /// serializing, this goes red, and the gate is the fix.
    /// </summary>
    [Fact]
    public async Task ConcurrentSendsOnOneSocket_AllArriveIntact()
    {
        var (server, client) = Pair();
        const int count = 100, size = 50_000;
        var received = new ConcurrentBag<byte[]>();

        var reader = Task.Run(async () =>
        {
            for (var i = 0; i < count; i++)
            {
                received.Add((await client.ReceiveMessageAsync(maxMessageBytes: size)).Data);
            }
        });

        await Task.WhenAll(Enumerable.Range(0, count).Select(i => Task.Run(() =>
            server.SendAsync(Enumerable.Repeat((byte)i, size).ToArray(), WebSocketMessageType.Binary, true, CancellationToken.None))));
        await reader.WaitAsync(TimeSpan.FromSeconds(15));

        received.Should().HaveCount(count);
        received.Should().OnlyContain(m => m.Length == size && m.All(b => b == m[0]), "no message may interleave with another");
        received.Select(m => m[0]).Distinct().Should().HaveCount(count);
    }

    // ---- WebSocketServer end to end ----

    private static int GetFreeLoopbackPort()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        try { return ((IPEndPoint)probe.LocalEndpoint).Port; }
        finally { probe.Stop(); }
    }

    [Fact]
    public async Task Server_ClientStreamingPastCapWithoutEndOfMessage_IsClosedWithMessageTooBig()
    {
        var server = new WebSocketServer { MaxMessageBytes = 64 * 1024 };
        var port = GetFreeLoopbackPort();
        var run = server.StartAsync($"http://localhost:{port}/");
        var disconnected = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        server.OnClientDisconnected += (_, id) => disconnected.TrySetResult(id);

        using var client = new ClientWebSocket();
        try
        {
            await client.ConnectAsync(new Uri($"ws://localhost:{port}/"), CancellationToken.None).WaitAsync(Timeout);

            // Keep sending fragments of one never-ending message, well past the cap, while watching for the close.
            var closeSeen = client.ReceiveAsync(new byte[64], CancellationToken.None);
            var chunk = new byte[16 * 1024];
            for (var i = 0; i < 64 && !closeSeen.IsCompleted; i++)
            {
                try { await client.SendAsync(chunk, WebSocketMessageType.Binary, endOfMessage: false, CancellationToken.None); }
                catch (WebSocketException) { break; }
            }

            var result = await closeSeen.WaitAsync(Timeout);
            result.MessageType.Should().Be(WebSocketMessageType.Close);
            client.CloseStatus.Should().Be(WebSocketCloseStatus.MessageTooBig);
            (await disconnected.Task.WaitAsync(Timeout)).Should().NotBeNullOrEmpty();
        }
        finally
        {
            await server.StopAsync();
            try { await run; } catch { /* listener teardown */ }
        }
    }

    [Fact]
    public void Server_NonPositiveMaxMessageBytes_Throws()
    {
        var server = new WebSocketServer();

        var act = () => server.MaxMessageBytes = 0;

        act.Should().Throw<ArgumentOutOfRangeException>();
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
