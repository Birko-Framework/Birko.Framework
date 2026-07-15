using System;
using System.Threading;
using System.Threading.Tasks;
using Birko.EventBus;
using Birko.EventBus.Local;
using Birko.EventBus.Pipeline;
using Birko.EventBus.Tests.TestResources;
using Birko.Rules;
using FluentAssertions;
using Xunit;

namespace Birko.EventBus.Tests
{
    /// <summary>
    /// CR-L250: InProcessEventSubscription.Dispose guarded _isActive with a non-atomic check-then-set,
    /// so concurrent/repeat disposes could unsubscribe twice — now an Interlocked check-and-clear.
    /// CR-L252: RuleFilterBehavior.BuildDefaultContext reflected over all public properties and called
    /// GetValue on each — an indexer threw TargetParameterCountException and a throwing getter surfaced
    /// its exception, failing the whole publish. Indexers are now skipped and throwing getters ignored.
    /// </summary>
    public class EventBusRobustnessTests
    {
        // ---- CR-L250 ----

        [Fact]
        public async Task Subscription_DoubleDispose_IsIdempotentAndStopsDelivery()
        {
            using var bus = new InProcessEventBus();
            var handler = new OrderPlacedHandler();
            var subscription = bus.Subscribe(handler);

            subscription.IsActive.Should().BeTrue();

            subscription.Dispose();
            Action secondDispose = () => subscription.Dispose();
            secondDispose.Should().NotThrow("a repeat dispose must be a safe no-op");
            subscription.IsActive.Should().BeFalse();

            await bus.PublishAsync(new OrderPlaced(Guid.NewGuid(), 1m));
            handler.ReceivedEvents.Should().BeEmpty("the handler was unsubscribed");
        }

        // ---- CR-L252 ----

        /// <summary>Event with an indexer and a throwing getter — the shapes that used to break filtering.</summary>
        private sealed record AwkwardEvent : EventBase
        {
            public override string Source => "awkward";
            public string Normal => "ok";
            public string Throws => throw new InvalidOperationException("getter boom");
            public int this[int index] => index; // indexer — GetValue would throw TargetParameterCountException
        }

        [Fact]
        public async Task RuleFilter_BuildDefaultContext_ToleratesIndexerAndThrowingGetter()
        {
            // Enabled rule set with no rules still builds the reflection context before evaluating.
            var behavior = new RuleFilterBehavior(new RuleSet("test"));
            var evt = new AwkwardEvent();

            var nextCalled = false;
            Func<Task> next = () => { nextCalled = true; return Task.CompletedTask; };

            await behavior.Invoking(b => b.HandleAsync(evt, EventContext.From(evt), next))
                .Should().NotThrowAsync("indexers are skipped and a throwing getter must not fail the pipeline");

            // No rules match → the event is filtered out (next not called), but crucially without throwing.
            nextCalled.Should().BeFalse();
        }
    }
}
