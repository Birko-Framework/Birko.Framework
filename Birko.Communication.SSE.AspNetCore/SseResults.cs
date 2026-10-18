using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;

namespace Birko.Communication.SSE.AspNetCore
{
    /// <summary>How an <see cref="SseResults"/> stream behaves.</summary>
    public sealed class SseStreamOptions
    {
        /// <summary>
        /// When nothing has been sent for this long, a comment frame (<c>: heartbeat</c>) is sent, so proxies and load
        /// balancers do not close an idle stream. Default 15 s; <see cref="Timeout.InfiniteTimeSpan"/> turns it off.
        /// </summary>
        public TimeSpan HeartbeatInterval { get; set; } = TimeSpan.FromSeconds(15);

        /// <summary>The heartbeat comment's text. Default <c>heartbeat</c>.</summary>
        public string HeartbeatComment { get; set; } = "heartbeat";

        /// <summary>A <c>retry:</c> reconnection delay sent once at the start, in milliseconds; null sends none.</summary>
        public int? RetryMilliseconds { get; set; }

        internal void Validate()
        {
            if (HeartbeatInterval != Timeout.InfiniteTimeSpan && HeartbeatInterval <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(HeartbeatInterval), HeartbeatInterval, "Must be positive, or Timeout.InfiniteTimeSpan to turn the heartbeat off.");
            if (string.IsNullOrEmpty(HeartbeatComment))
                throw new ArgumentException("A heartbeat comment is required.", nameof(HeartbeatComment));
            if (RetryMilliseconds is < 0)
                throw new ArgumentOutOfRangeException(nameof(RetryMilliseconds), RetryMilliseconds, "Must not be negative.");
        }
    }

    /// <summary>
    /// Minimal-API results that stream Server-Sent Events from a channel or an async sequence, with a heartbeat, a terminal
    /// stop and proxy-friendly headers (TASK-540). <c>TypedResults.ServerSentEvents</c> has neither the heartbeat nor the stop.
    /// </summary>
    /// <remarks>
    /// Every frame is <see cref="SseEvent.ToString"/>, the heartbeat included, so the frame text has one producer.
    /// <para>
    /// The stream ends when the source completes, when an item matches <c>isTerminal</c> (it is sent first), or when the
    /// client disconnects. <c>owns</c> (e.g. the broadcaster subscription the source reads) is disposed in every case.
    /// Subscribe <b>before</b> returning the result, so nothing published between the request and the first write is lost:
    /// <code>
    /// app.MapGet("/runs/{id}/events", (Guid id, KeyedEventBroadcaster&lt;Guid, RunEvent&gt; live) =&gt;
    /// {
    ///     var subscription = live.Subscribe(id);
    ///     return SseResults.Stream(subscription.Reader,
    ///         e =&gt; SseEvent.FromJson(e.Event, e.Event.Kind, e.Sequence.ToString()),
    ///         isTerminal: e =&gt; e.Event.IsFinal, owns: subscription);
    /// });
    /// </code>
    /// </para>
    /// </remarks>
    public static class SseResults
    {
        /// <summary>Streams the items of <paramref name="reader"/> until it completes, a terminal item, or a disconnect.</summary>
        public static IResult Stream<T>(
            ChannelReader<T> reader,
            Func<T, SseEvent> toEvent,
            Func<T, bool>? isTerminal = null,
            SseStreamOptions? options = null,
            IDisposable? owns = null)
        {
            if (reader == null) throw new ArgumentNullException(nameof(reader));
            return Stream(ct => reader.ReadAllAsync(ct), toEvent, isTerminal, options, owns);
        }

        /// <summary>Streams the items of <paramref name="source"/> until it ends, a terminal item, or a disconnect.</summary>
        public static IResult Stream<T>(
            IAsyncEnumerable<T> source,
            Func<T, SseEvent> toEvent,
            Func<T, bool>? isTerminal = null,
            SseStreamOptions? options = null,
            IDisposable? owns = null)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            return Stream(_ => source, toEvent, isTerminal, options, owns);
        }

        /// <summary>Streams <see cref="SseEvent"/>s as they are.</summary>
        public static IResult Stream(
            IAsyncEnumerable<SseEvent> source,
            Func<SseEvent, bool>? isTerminal = null,
            SseStreamOptions? options = null,
            IDisposable? owns = null)
            => Stream(source, e => e, isTerminal, options, owns);

        private static IResult Stream<T>(
            Func<CancellationToken, IAsyncEnumerable<T>> source,
            Func<T, SseEvent> toEvent,
            Func<T, bool>? isTerminal,
            SseStreamOptions? options,
            IDisposable? owns)
        {
            if (toEvent == null) throw new ArgumentNullException(nameof(toEvent));
            var resolved = options ?? new SseStreamOptions();
            resolved.Validate();
            return new SseStreamResult<T>(source, toEvent, isTerminal, resolved, owns);
        }

        private sealed class SseStreamResult<T> : IResult
        {
            private readonly Func<CancellationToken, IAsyncEnumerable<T>> _source;
            private readonly Func<T, SseEvent> _toEvent;
            private readonly Func<T, bool>? _isTerminal;
            private readonly SseStreamOptions _options;
            private readonly IDisposable? _owns;

            public SseStreamResult(Func<CancellationToken, IAsyncEnumerable<T>> source, Func<T, SseEvent> toEvent,
                Func<T, bool>? isTerminal, SseStreamOptions options, IDisposable? owns)
            {
                _source = source;
                _toEvent = toEvent;
                _isTerminal = isTerminal;
                _options = options;
                _owns = owns;
            }

            public async Task ExecuteAsync(HttpContext httpContext)
            {
                var ct = httpContext.RequestAborted;
                try
                {
                    var response = httpContext.Response;
                    response.StatusCode = StatusCodes.Status200OK;
                    response.ContentType = "text/event-stream";
                    response.Headers.CacheControl = "no-cache";
                    // nginx buffers a proxied response unless told not to, which would hold every frame back.
                    response.Headers["X-Accel-Buffering"] = "no";
                    httpContext.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();

                    await response.StartAsync(ct).ConfigureAwait(false);
                    if (_options.RetryMilliseconds is { } retry)
                        await WriteAsync(response, new SseEvent { Retry = retry }, ct).ConfigureAwait(false);
                    else
                        await response.Body.FlushAsync(ct).ConfigureAwait(false);

                    await PumpAsync(response, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    // The client went away: the stream is over, nothing to report.
                }
                finally
                {
                    _owns?.Dispose();
                }
            }

            private async Task PumpAsync(HttpResponse response, CancellationToken ct)
            {
                var heartbeat = SseEvent.CreateComment(_options.HeartbeatComment);
                var items = _source(ct).GetAsyncEnumerator(ct);
                var move = new PendingMove();
                try
                {
                    await PumpAsync(response, items, heartbeat, move, ct).ConfigureAwait(false);
                }
                finally
                {
                    if (move.Task is { IsCompleted: false } pending)
                    {
                        // A disconnect can leave a MoveNext in flight, and an enumerator must not be disposed under it
                        // (an async iterator throws). Dispose once it settles, observing whatever it ended with.
                        _ = pending.ContinueWith(async t => { _ = t.Exception; await DisposeQuietlyAsync(items).ConfigureAwait(false); },
                            CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default).Unwrap();
                    }
                    else
                    {
                        await DisposeQuietlyAsync(items).ConfigureAwait(false);
                    }
                }
            }

            private static async Task DisposeQuietlyAsync(IAsyncEnumerator<T> items)
            {
                try
                {
                    await items.DisposeAsync().ConfigureAwait(false);
                }
                catch (Exception)
                {
                    // The stream is already over; a source that fails to clean up has nothing left to tell the client.
                }
            }

            /// <summary>The MoveNext in flight, kept across heartbeats: an enumerator may not be advanced twice at once.</summary>
            private sealed class PendingMove
            {
                public Task<bool>? Task;
            }

            private async Task PumpAsync(HttpResponse response, IAsyncEnumerator<T> items, SseEvent heartbeat, PendingMove move, CancellationToken ct)
            {
                while (true)
                {
                    var next = move.Task ??= items.MoveNextAsync().AsTask();
                    if (_options.HeartbeatInterval != Timeout.InfiniteTimeSpan && !next.IsCompleted)
                    {
                        var idle = System.Threading.Tasks.Task.Delay(_options.HeartbeatInterval, ct);
                        if (await System.Threading.Tasks.Task.WhenAny(next, idle).ConfigureAwait(false) == idle)
                        {
                            ct.ThrowIfCancellationRequested();
                            await WriteAsync(response, heartbeat, ct).ConfigureAwait(false);
                            continue;
                        }
                    }

                    var hasItem = await next.ConfigureAwait(false);
                    move.Task = null;
                    if (!hasItem) return;

                    var item = items.Current;
                    await WriteAsync(response, _toEvent(item), ct).ConfigureAwait(false);
                    if (_isTerminal?.Invoke(item) == true) return;
                }
            }

            private static async Task WriteAsync(HttpResponse response, SseEvent frame, CancellationToken ct)
            {
                await response.WriteAsync(frame.ToString(), ct).ConfigureAwait(false);
                await response.Body.FlushAsync(ct).ConfigureAwait(false);
            }
        }
    }
}
