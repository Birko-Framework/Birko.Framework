using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Birko.Communication.SSE;
using Birko.Communication.SSE.AspNetCore;
using Birko.EventBus.Live;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Birko.Communication.SSE.AspNetCore.Tests;

/// <summary>
/// TASK-540: <see cref="SseResults"/> over a TestServer. Frames are compared with <see cref="SseEvent.ToString"/>, the one
/// producer of the frame text.
/// </summary>
public class SseResultsTests : IAsyncLifetime
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);
    private WebApplication _app = null!;
    private HttpClient _client = null!;
    private Func<HttpContext, IResult> _endpoint = _ => Results.Ok();

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        _app = builder.Build();
        _app.MapGet("/sse", (HttpContext c) => _endpoint(c));
        await _app.StartAsync();
        _client = _app.GetTestClient();
    }

    public async Task DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
    }

    private sealed class Tracked : IDisposable
    {
        public TaskCompletionSource Disposed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Dispose() => Disposed.TrySetResult();
    }

    private async Task<string> ReadAllAsync()
    {
        using var response = await _client.GetAsync("/sse", HttpCompletionOption.ResponseHeadersRead).WaitAsync(Timeout);
        return await response.Content.ReadAsStringAsync().WaitAsync(Timeout);
    }

    [Fact]
    public async Task Frames_are_written_with_proxy_friendly_headers_and_the_stream_ends_with_the_source()
    {
        var channel = Channel.CreateUnbounded<int>();
        channel.Writer.TryWrite(1);
        channel.Writer.TryWrite(2);
        channel.Writer.Complete();
        var owned = new Tracked();
        _endpoint = _ => SseResults.Stream(channel.Reader, n => SseEvent.Create($"n{n}", "tick", n.ToString()), owns: owned);

        using var response = await _client.GetAsync("/sse", HttpCompletionOption.ResponseHeadersRead).WaitAsync(Timeout);
        var body = await response.Content.ReadAsStringAsync().WaitAsync(Timeout);

        response.Content.Headers.ContentType!.MediaType.Should().Be("text/event-stream");
        response.Headers.CacheControl!.NoCache.Should().BeTrue();
        response.Headers.GetValues("X-Accel-Buffering").Should().Equal("no");
        body.Should().Be(SseEvent.Create("n1", "tick", "1").ToString() + SseEvent.Create("n2", "tick", "2").ToString());
        await owned.Disposed.Task.WaitAsync(Timeout);
    }

    [Fact]
    public async Task A_terminal_item_is_sent_and_ends_the_stream()
    {
        var channel = Channel.CreateUnbounded<string>();
        foreach (var s in new[] { "a", "done", "after" }) channel.Writer.TryWrite(s);
        var owned = new Tracked();
        _endpoint = _ => SseResults.Stream(channel.Reader, s => SseEvent.Create(s), isTerminal: s => s == "done", owns: owned);

        var body = await ReadAllAsync();

        body.Should().Be(SseEvent.Create("a").ToString() + SseEvent.Create("done").ToString(), "the channel is never completed: the stop is the terminal item");
        await owned.Disposed.Task.WaitAsync(Timeout);
    }

    [Fact]
    public async Task An_idle_stream_sends_heartbeats_and_keeps_the_item_it_was_waiting_for()
    {
        var channel = Channel.CreateUnbounded<string>();
        _endpoint = _ => SseResults.Stream(channel.Reader, s => SseEvent.Create(s), isTerminal: _ => true,
            options: new SseStreamOptions { HeartbeatInterval = TimeSpan.FromMilliseconds(40) });

        using var response = await _client.GetAsync("/sse", HttpCompletionOption.ResponseHeadersRead).WaitAsync(Timeout);
        await using var stream = await response.Content.ReadAsStreamAsync();
        using var reader = new StreamReader(stream);
        var heartbeat = SseEvent.CreateComment("heartbeat").ToString();
        var first = await ReadFrameAsync(reader);
        var second = await ReadFrameAsync(reader);
        channel.Writer.TryWrite("late");
        var rest = await reader.ReadToEndAsync().WaitAsync(Timeout);

        first.Should().Be(heartbeat);
        second.Should().Be(heartbeat);
        rest.Should().EndWith(SseEvent.Create("late").ToString(), "the item that arrived after heartbeats is delivered");
    }

    [Fact]
    public async Task A_retry_delay_is_sent_once_at_the_start()
    {
        var channel = Channel.CreateUnbounded<string>();
        channel.Writer.TryWrite("x");
        channel.Writer.Complete();
        _endpoint = _ => SseResults.Stream(channel.Reader, s => SseEvent.Create(s), options: new SseStreamOptions { RetryMilliseconds = 3000 });

        var body = await ReadAllAsync();

        body.Should().Be(new SseEvent { Retry = 3000 }.ToString() + SseEvent.Create("x").ToString());
    }

    [Fact]
    public async Task A_client_disconnect_ends_the_loop_and_releases_the_source()
    {
        var owned = new Tracked();
        var enumerationEnded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _endpoint = _ => SseResults.Stream(Endless(enumerationEnded), n => SseEvent.Create(n.ToString()),
            options: new SseStreamOptions { HeartbeatInterval = TimeSpan.FromMilliseconds(20) }, owns: owned);

        var cts = new CancellationTokenSource();
        var response = await _client.GetAsync("/sse", HttpCompletionOption.ResponseHeadersRead, cts.Token).WaitAsync(Timeout);
        var stream = await response.Content.ReadAsStreamAsync();
        await stream.ReadAsync(new byte[16]).AsTask().WaitAsync(Timeout);
        cts.Cancel();
        response.Dispose();

        await owned.Disposed.Task.WaitAsync(Timeout);
        await enumerationEnded.Task.WaitAsync(Timeout);
    }

    private static async IAsyncEnumerable<int> Endless(TaskCompletionSource ended, [EnumeratorCancellation] CancellationToken ct = default)
    {
        try
        {
            for (var i = 0; ; i++)
            {
                await Task.Delay(10, ct);
                yield return i;
            }
        }
        finally
        {
            ended.TrySetResult();
        }
    }

    [Fact]
    public async Task A_keyed_broadcaster_feeds_the_stream_without_glue()
    {
        var live = new KeyedEventBroadcaster<string, string>();
        var owned = new Tracked();
        _endpoint = _ =>
        {
            var subscription = live.Subscribe("run-1"); // subscribe first, then return the result
            owned = new Tracked();
            return SseResults.Stream(subscription.Reader, e => SseEvent.Create(e.Event, "run", e.Sequence.ToString()),
                isTerminal: e => e.Event == "finished", owns: new Both(subscription, owned));
        };

        var read = ReadAllAsync();
        await WaitUntil(() => live.SubscriberCount("run-1") == 1);
        live.Publish("run-1", "started");
        live.Publish("run-2", "other run");
        live.Publish("run-1", "finished");
        var body = await read;

        body.Should().Be(SseEvent.Create("started", "run", "1").ToString() + SseEvent.Create("finished", "run", "2").ToString());
        await owned.Disposed.Task.WaitAsync(Timeout);
        live.SubscriberCount("run-1").Should().Be(0, "ending the stream disposes the subscription");
    }

    private sealed class Both(IDisposable a, IDisposable b) : IDisposable
    {
        public void Dispose() { a.Dispose(); b.Dispose(); }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void A_non_positive_heartbeat_is_refused(int ms)
    {
        var act = () => SseResults.Stream(Channel.CreateUnbounded<int>().Reader, n => SseEvent.Create(""),
            options: new SseStreamOptions { HeartbeatInterval = TimeSpan.FromMilliseconds(ms) });

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    private static async Task<string> ReadFrameAsync(StreamReader reader)
    {
        var frame = new StringBuilder();
        while (true)
        {
            var line = await reader.ReadLineAsync().WaitAsync(Timeout) ?? throw new EndOfStreamException();
            frame.Append(line).Append('\n');
            if (line.Length == 0) return frame.ToString();
        }
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + Timeout;
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException();
            await Task.Delay(10);
        }
    }
}
