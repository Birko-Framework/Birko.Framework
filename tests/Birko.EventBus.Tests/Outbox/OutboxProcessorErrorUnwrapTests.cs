using System;
using System.Threading;
using System.Threading.Tasks;
using Birko.EventBus.Local;
using Birko.EventBus.Outbox;
using Birko.EventBus.Outbox.Publishing;
using Birko.EventBus.Outbox.Stores;
using Birko.EventBus.Tests.TestResources;
using FluentAssertions;
using Xunit;

namespace Birko.EventBus.Tests.Outbox
{
    /// <summary>
    /// CR-M189: PublishEventAsync calls PublishAsync via MethodInfo.Invoke; a synchronous throw is wrapped
    /// in TargetInvocationException whose Message is the opaque "Exception has been thrown by the target of
    /// an invocation." The processor now unwraps it so the outbox entry's LastError records the real cause.
    /// </summary>
    public class OutboxProcessorErrorUnwrapTests
    {
        /// <summary>An IEventBus whose PublishAsync throws synchronously with a distinctive message.</summary>
        private sealed class FaultingBus : IEventBus
        {
            public Task PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default) where TEvent : IEvent
                => throw new InvalidOperationException("distinctive publish failure");
            public IEventSubscription Subscribe<TEvent>(IEventHandler<TEvent> handler) where TEvent : IEvent
                => throw new NotSupportedException();
            public void Dispose() { }
        }

        [Fact]
        public async Task PublishFailure_RecordsUnderlyingCause_NotReflectionWrapper()
        {
            var store = new InMemoryOutboxStore();

            // Write a real, resolvable + deserializable entry via the outbox bus.
            using var writeBus = new InProcessEventBus();
            using var outboxBus = new OutboxEventBus(writeBus, store);
            await outboxBus.PublishAsync(new OrderPlaced(Guid.NewGuid(), 1m));

            // Process against a publisher that throws synchronously (goes through MethodInfo.Invoke).
            var processor = new OutboxProcessor(store, new FaultingBus());
            await processor.ProcessBatchAsync();

            var entry = store.GetAll().Should().ContainSingle().Subject;
            entry.LastError.Should().Contain("distinctive publish failure", "the real cause must be recorded (CR-M189)");
            entry.LastError.Should().NotContain("target of an invocation", "the opaque reflection wrapper must be unwrapped");
        }
    }
}
