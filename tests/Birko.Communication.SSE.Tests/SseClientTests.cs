using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Birko.Communication.SSE;
using FluentAssertions;
using Xunit;

namespace Birko.Communication.SSE.Tests;

/// <summary>
/// CR-M070 / CR-M071: SseClient now does REAL HTTP SSE streaming — ConnectAsync opens a streaming
/// GET, EnsureSuccessStatusCode surfaces failures to the caller, ReceiveLoop parses lines into
/// events, and a repeat ConnectAsync tears down the prior session instead of orphaning it. All
/// exercised offline over a mock HttpMessageHandler (no real network).
/// </summary>
public class SseClientTests
{
    private sealed class MockHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;
        public int CallCount;

        public MockHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref CallCount);
            return Task.FromResult(_responder(request));
        }
    }

    // Fresh stream per call: the receive loop consumes the body, and a reconnect/repeat re-requests.
    private static HttpResponseMessage EventStream(string body)
    {
        var content = new StreamContent(new MemoryStream(Encoding.UTF8.GetBytes(body)));
        content.Headers.ContentType = new MediaTypeHeaderValue("text/event-stream");
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
    }

    private static async Task WithTimeout(Task task, string because)
    {
        var completed = await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(5)));
        completed.Should().Be(task, because);
        await task; // surface any exception / observe completion
    }

    [Fact]
    public async Task ConnectAsync_StreamsEvents_RaisesOnConnectedOnMessageOnEvent_AndTracksLastEventId()
    {
        // Two events: the first carries an id, the second is data-only.
        const string body = "id: 1\ndata: hello\n\ndata: world\n\n";
        var handler = new MockHandler(_ => EventStream(body));
        using var http = new HttpClient(handler);
        using var client = new SseClient("http://localhost/sse", null, http) { AutoReconnect = false };

        var messages = new List<string>();
        var events = new List<SseEvent>();
        var connected = false;
        var bothReceived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        client.OnConnected += (_, _) => connected = true;
        client.OnEvent += (_, e) => { lock (events) events.Add(e); };
        client.OnMessage += (_, m) =>
        {
            lock (messages)
            {
                messages.Add(m);
                if (messages.Count >= 2)
                {
                    bothReceived.TrySetResult();
                }
            }
        };

        await client.ConnectAsync();

        // Bounded wait — never an unbounded block if the fix regresses.
        await WithTimeout(bothReceived.Task, "both streamed events should be dispatched within the timeout");

        connected.Should().BeTrue("OnConnected fires once the stream opens successfully");
        messages.Should().ContainInOrder("hello", "world");
        client.LastEventId.Should().Be("1", "LastEventId tracks the id: line");
        events.Should().HaveCountGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task ConnectAsync_ThrowsOnNonSuccessStatus()
    {
        var handler = new MockHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        using var http = new HttpClient(handler);
        using var client = new SseClient("http://localhost/sse", null, http) { AutoReconnect = false };

        var act = () => client.ConnectAsync();

        await act.Should().ThrowAsync<HttpRequestException>("a 404 must surface via EnsureSuccessStatusCode (CR-M071)");
        client.IsConnected.Should().BeFalse();
    }

    [Fact]
    public async Task ConnectAsync_CalledTwice_TearsDownPriorSession_WithoutThrowingOrDeadlocking()
    {
        var handler = new MockHandler(_ => EventStream("data: x\n\n"));
        using var http = new HttpClient(handler);
        using var client = new SseClient("http://localhost/sse", null, http) { AutoReconnect = false };

        await client.ConnectAsync();

        // The second connect must disconnect the prior session first (CR-M070) — bounded so a
        // deadlock regression fails fast instead of hanging the suite. (IsConnected is not asserted
        // here: with AutoReconnect off the short body ends immediately, so it races back to false.)
        await WithTimeout(client.ConnectAsync(), "a repeat ConnectAsync must not deadlock");

        handler.CallCount.Should().BeGreaterThanOrEqualTo(2, "both the first and the repeat connect opened a stream");
    }
}
