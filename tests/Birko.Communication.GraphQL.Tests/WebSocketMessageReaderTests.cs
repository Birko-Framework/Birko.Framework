using System;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace Birko.Communication.GraphQL.Tests;

/// <summary>
/// Regression for CR-H021: the subscription/ack receive used a single fixed 8 KB buffer and parsed
/// immediately, so a fragmented or &gt; 8 KB message was truncated. WebSocketMessageReader now
/// accumulates fragments until EndOfMessage. Exercised with a scripted fake WebSocket.
/// </summary>
public class WebSocketMessageReaderTests
{
    private sealed class ScriptedWebSocket : WebSocket
    {
        private readonly Queue<(byte[] Data, bool End, WebSocketMessageType Type)> _frames;
        public ScriptedWebSocket(IEnumerable<(byte[], bool, WebSocketMessageType)> frames) => _frames = new(frames);

        public override Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken ct)
        {
            var (data, end, type) = _frames.Dequeue();
            var n = Math.Min(data.Length, buffer.Count);
            Array.Copy(data, 0, buffer.Array!, buffer.Offset, n);
            // For simplicity each scripted frame fits the 8 KB buffer.
            return Task.FromResult(new WebSocketReceiveResult(n, type, end));
        }

        public override WebSocketState State => WebSocketState.Open;
        public override WebSocketCloseStatus? CloseStatus => null;
        public override string? CloseStatusDescription => null;
        public override string? SubProtocol => null;
        public override void Abort() { }
        public override Task CloseAsync(WebSocketCloseStatus s, string? d, CancellationToken ct) => Task.CompletedTask;
        public override Task CloseOutputAsync(WebSocketCloseStatus s, string? d, CancellationToken ct) => Task.CompletedTask;
        public override void Dispose() { }
        public override Task SendAsync(ArraySegment<byte> b, WebSocketMessageType t, bool e, CancellationToken ct) => Task.CompletedTask;
    }

    [Fact]
    public async Task ReceiveTextAsync_ConcatenatesFragments()
    {
        var text = "{\"type\":\"next\",\"payload\":{\"data\":{\"x\":1}}}";
        var b = Encoding.UTF8.GetBytes(text);
        var mid = b.Length / 2;
        var socket = new ScriptedWebSocket(new[]
        {
            (b[..mid], false, WebSocketMessageType.Text),
            (b[mid..], true, WebSocketMessageType.Text),
        });

        var msg = await WebSocketMessageReader.ReceiveTextAsync(socket, CancellationToken.None);

        msg.Type.Should().Be(WebSocketMessageType.Text);
        msg.Text.Should().Be(text);
    }

    [Fact]
    public async Task ReceiveTextAsync_HandlesMessageLargerThanBuffer()
    {
        // A payload well beyond a single 8 KB frame, split into three fragments.
        var big = new string('a', 20_000);
        var text = $"{{\"v\":\"{big}\"}}";
        var b = Encoding.UTF8.GetBytes(text);
        var third = b.Length / 3;
        var socket = new ScriptedWebSocket(new[]
        {
            (b[..third], false, WebSocketMessageType.Text),
            (b[third..(2 * third)], false, WebSocketMessageType.Text),
            (b[(2 * third)..], true, WebSocketMessageType.Text),
        });

        var msg = await WebSocketMessageReader.ReceiveTextAsync(socket, CancellationToken.None);

        msg.Text.Should().Be(text);
        msg.Text!.Length.Should().BeGreaterThan(8192);
    }

    [Fact]
    public async Task ReceiveTextAsync_ReturnsCloseType()
    {
        var socket = new ScriptedWebSocket(new[]
        {
            (Array.Empty<byte>(), true, WebSocketMessageType.Close),
        });

        var msg = await WebSocketMessageReader.ReceiveTextAsync(socket, CancellationToken.None);

        msg.Type.Should().Be(WebSocketMessageType.Close);
        msg.Text.Should().BeNull();
    }
}
