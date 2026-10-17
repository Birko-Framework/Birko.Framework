using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Birko.EventBus.Live;
using FluentAssertions;
using Xunit;

namespace Birko.EventBus.Tests
{
    /// <summary>
    /// TASK-533 — <see cref="KeyedEventBroadcaster{TKey, TEvent}"/>: non-blocking publish, a bounded drop-oldest buffer per
    /// subscriber, a per-key sequence, subscribe-before-publish, completion. Backported from DraCode's
    /// <c>KoboldRunEventSource</c>, which wrote outside the lock that stamped the sequence (so concurrent publishers could
    /// reorder) and showed a drop only as a gap.
    /// </summary>
    public class KeyedEventBroadcasterTests
    {
        private static async Task<List<LiveEvent<T>>> Drain<T>(LiveEventSubscription<T> subscription)
        {
            var events = new List<LiveEvent<T>>();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await foreach (var e in subscription.ReadAllAsync(cts.Token)) events.Add(e);
            return events;
        }

        [Fact]
        public async Task A_subscriber_attached_before_the_first_event_receives_everything_in_order()
        {
            var broadcaster = new KeyedEventBroadcaster<Guid, string>();
            var run = Guid.NewGuid();
            using var subscription = broadcaster.Subscribe(run);

            broadcaster.Publish(run, "a").Should().BeTrue();
            broadcaster.Publish(run, "b").Should().BeTrue();
            broadcaster.Complete(run);

            (await Drain(subscription)).Should().Equal(new LiveEvent<string>(1, "a"), new LiveEvent<string>(2, "b"));
            subscription.Dropped.Should().Be(0);
        }

        [Fact]
        public async Task A_slow_subscriber_blocks_neither_the_publisher_nor_another_subscriber_and_sees_its_drops()
        {
            var broadcaster = new KeyedEventBroadcaster<string, int>(capacity: 4);
            using var slow = broadcaster.Subscribe("run");     // never read until the end
            using var fast = broadcaster.Subscribe("run");
            var fastEvents = new List<LiveEvent<int>>();
            var reading = Task.Run(async () => { await foreach (var e in fast.ReadAllAsync()) fastEvents.Add(e); });

            var watch = Stopwatch.StartNew();
            for (var i = 1; i <= 1000; i++)
            {
                broadcaster.Publish("run", i);
                if (i % 4 == 0) await Task.Yield(); // let the fast reader keep up with a capacity of 4
                while (fast.Reader.Count >= 4) await Task.Delay(1);
            }
            watch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5));
            broadcaster.Complete("run");
            await reading;

            fastEvents.Select(e => e.Event).Should().Equal(Enumerable.Range(1, 1000), "the fast subscriber kept up");
            fast.Dropped.Should().Be(0);

            var slowEvents = await Drain(slow);
            slowEvents.Select(e => e.Sequence).Should().Equal(997, 998, 999, 1000);
            slow.Dropped.Should().Be(996, "the drop is counted, and shows as the gap before sequence 997");
        }

        [Fact]
        public async Task Concurrent_publishers_never_reorder_a_subscribers_sequence()
        {
            const int threads = 8, perThread = 2000;
            var broadcaster = new KeyedEventBroadcaster<int, int>(capacity: threads * perThread);
            using var subscription = broadcaster.Subscribe(1);
            using var start = new Barrier(threads);

            await Task.WhenAll(Enumerable.Range(0, threads).Select(t => Task.Run(() =>
            {
                start.SignalAndWait();
                for (var i = 0; i < perThread; i++) broadcaster.Publish(1, t);
            })));
            broadcaster.Complete(1);

            var sequences = (await Drain(subscription)).Select(e => e.Sequence).ToList();
            sequences.Should().Equal(Enumerable.Range(1, threads * perThread).Select(i => (long)i));
        }

        [Fact]
        public async Task Completing_a_key_ends_its_readers_and_an_error_reaches_them()
        {
            var broadcaster = new KeyedEventBroadcaster<string, string>();
            using var ok = broadcaster.Subscribe("ok");
            using var failed = broadcaster.Subscribe("failed");
            broadcaster.Publish("failed", "partial");

            broadcaster.Complete("ok");
            broadcaster.Complete("failed", new InvalidOperationException("run failed"));

            (await Drain(ok)).Should().BeEmpty();
            var read = new List<string>();
            var act = async () => { await foreach (var e in failed.ReadAllAsync()) read.Add(e.Event); };
            await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("run failed");
            read.Should().Equal("partial");
            broadcaster.SubscriberCount("ok").Should().Be(0);
        }

        [Fact]
        public void An_event_for_a_key_nobody_watches_goes_nowhere_and_creates_nothing()
        {
            var broadcaster = new KeyedEventBroadcaster<string, int>();
            broadcaster.Publish("nobody", 1).Should().BeFalse();
            broadcaster.SubscriberCount("nobody").Should().Be(0);
            broadcaster.Complete("nobody"); // no-op
        }

        [Fact]
        public async Task Disposing_detaches_and_the_last_one_out_forgets_the_key()
        {
            var broadcaster = new KeyedEventBroadcaster<string, int>();
            var first = broadcaster.Subscribe("k");
            var second = broadcaster.Subscribe("k");
            broadcaster.Publish("k", 1);

            first.Dispose();
            first.Dispose(); // idempotent
            broadcaster.SubscriberCount("k").Should().Be(1);
            broadcaster.Publish("k", 2).Should().BeTrue();
            (await Drain(first)).Select(e => e.Event).Should().Equal(1);

            second.Dispose();
            broadcaster.SubscriberCount("k").Should().Be(0);
            broadcaster.Publish("k", 3).Should().BeFalse();

            using var third = broadcaster.Subscribe("k");
            broadcaster.Publish("k", 4);
            broadcaster.Complete("k");
            (await Drain(third)).Should().Equal(new LiveEvent<int>(1, 4)); // the forgotten key starts again at 1
        }

        [Fact]
        public async Task Keys_are_independent()
        {
            var broadcaster = new KeyedEventBroadcaster<string, string>();
            using var a = broadcaster.Subscribe("a");
            using var b = broadcaster.Subscribe("b");
            broadcaster.Publish("a", "for-a");
            broadcaster.Publish("b", "for-b");
            broadcaster.Publish("b", "for-b-2");
            broadcaster.Complete("a");
            broadcaster.Complete("b");

            (await Drain(a)).Should().Equal(new LiveEvent<string>(1, "for-a"));
            (await Drain(b)).Select(e => e.Sequence).Should().Equal(1, 2);
        }

        [Fact]
        public void Publish_racing_complete_and_dispose_never_throws()
        {
            var broadcaster = new KeyedEventBroadcaster<int, int>(capacity: 2);
            var act = () => Parallel.For(0, 2000, i =>
            {
                var key = i % 5;
                if (i % 7 == 0) broadcaster.Complete(key);
                using var s = broadcaster.Subscribe(key);
                broadcaster.Publish(key, i);
            });
            act.Should().NotThrow();
        }

        [Fact]
        public void A_buffer_needs_room_for_one_event()
        {
            var act = () => new KeyedEventBroadcaster<int, int>(capacity: 0);
            act.Should().Throw<ArgumentOutOfRangeException>();
        }
    }
}
