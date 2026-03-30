using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Birko.EventBus;
using Birko.EventBus.Deduplication;
using Birko.EventBus.Local;
using Birko.EventBus.MessageQueue;
using Birko.EventBus.Outbox;
using Birko.EventBus.Outbox.Publishing;
using Birko.EventBus.Outbox.Stores;
using Birko.EventBus.Pipeline;
using Birko.EventBus.Routing;
using Birko.MessageQueue.InMemory;

namespace Birko.Framework.Examples.EventBus
{
    // ── Sample Events ──────────────────────────────

    public sealed record OrderPlaced(Guid OrderId, decimal Total) : EventBase
    {
        public override string Source => "orders";
    }

    public sealed record OrderShipped(Guid OrderId, string TrackingNumber) : EventBase
    {
        public override string Source => "shipping";
    }

    [Topic("custom.alerts.low-stock")]
    public sealed record LowStockAlert(Guid ProductId, int Remaining) : EventBase
    {
        public override string Source => "inventory";
    }

    // ── Sample Handlers ────────────────────────────

    public class OrderPlacedHandler : IEventHandler<OrderPlaced>
    {
        public Task HandleAsync(OrderPlaced @event, EventContext context, CancellationToken ct = default)
        {
            ExampleOutput.WriteSuccess($"Handler received: Order {@event.OrderId} for {@event.Total:C}");
            if (context.CorrelationId.HasValue)
                ExampleOutput.WriteInfo("  CorrelationId", context.CorrelationId.Value.ToString());
            return Task.CompletedTask;
        }
    }

    public class OrderShippedHandler : IEventHandler<OrderShipped>
    {
        public Task HandleAsync(OrderShipped @event, EventContext context, CancellationToken ct = default)
        {
            ExampleOutput.WriteSuccess($"Handler received: Order {@event.OrderId} shipped ({@event.TrackingNumber})");
            return Task.CompletedTask;
        }
    }

    // ── Sample Pipeline Behavior ───────────────────

    public class LoggingBehavior : IEventPipelineBehavior
    {
        public async Task HandleAsync(IEvent @event, EventContext context, Func<Task> next, CancellationToken ct = default)
        {
            ExampleOutput.WriteInfo("  Pipeline", $"Before: {@event.GetType().Name} (EventId: {@event.EventId:N})");
            await next();
            ExampleOutput.WriteInfo("  Pipeline", $"After: {@event.GetType().Name}");
        }
    }

    // ── Examples ───────────────────────────────────

    public static class EventBusExamples
    {
        /// <summary>
        /// Basic in-process publish/subscribe.
        /// </summary>
        public static async Task RunInProcessExample()
        {
            ExampleOutput.WriteHeader("In-Process Event Bus");
            ExampleOutput.WriteLine();

            using var bus = new InProcessEventBus();

            // Subscribe handlers
            bus.Subscribe(new OrderPlacedHandler());
            bus.Subscribe(new OrderShippedHandler());

            ExampleOutput.WriteInfo("Subscribed", "OrderPlacedHandler + OrderShippedHandler");
            ExampleOutput.WriteLine();

            // Publish events
            await bus.PublishAsync(new OrderPlaced(Guid.NewGuid(), 99.99m));
            await bus.PublishAsync(new OrderShipped(Guid.NewGuid(), "TRACK-001"));

            ExampleOutput.WriteLine();
            ExampleOutput.WriteSuccess("Both events dispatched to matching handlers.");
        }

        /// <summary>
        /// Pipeline behaviors wrapping handler execution.
        /// </summary>
        public static async Task RunPipelineExample()
        {
            ExampleOutput.WriteHeader("Pipeline Behaviors");
            ExampleOutput.WriteLine();

            using var bus = new InProcessEventBus(
                behaviors: new IEventPipelineBehavior[] { new LoggingBehavior() });

            bus.Subscribe(new OrderPlacedHandler());

            ExampleOutput.WriteInfo("Pipeline", "LoggingBehavior registered");
            ExampleOutput.WriteLine();

            await bus.PublishAsync(new OrderPlaced(Guid.NewGuid(), 50m) { CorrelationId = Guid.NewGuid() });

            ExampleOutput.WriteLine();
            ExampleOutput.WriteSuccess("Event dispatched through pipeline → handler.");
        }

        /// <summary>
        /// Deduplication — same event published twice, handled once.
        /// </summary>
        public static async Task RunDeduplicationExample()
        {
            ExampleOutput.WriteHeader("Deduplication");
            ExampleOutput.WriteLine();

            var store = new InMemoryDeduplicationStore();
            var behavior = new DeduplicationBehavior(store);
            var handlerCount = 0;

            using var bus = new InProcessEventBus(behaviors: new IEventPipelineBehavior[] { behavior });
            bus.Subscribe(new CountingHandler(() => Interlocked.Increment(ref handlerCount)));

            var evt = new OrderPlaced(Guid.NewGuid(), 25m);

            ExampleOutput.WriteInfo("Publish", $"Event {evt.EventId:N} (first time)");
            await bus.PublishAsync(evt);

            ExampleOutput.WriteInfo("Publish", $"Event {evt.EventId:N} (duplicate)");
            await bus.PublishAsync(evt);

            ExampleOutput.WriteLine();
            ExampleOutput.WriteSuccess($"Handler invoked {handlerCount} time(s) — duplicate was skipped.");
        }

        /// <summary>
        /// Topic conventions — default kebab-case and attribute-based.
        /// </summary>
        public static void RunTopicConventionExample()
        {
            ExampleOutput.WriteHeader("Topic Conventions");
            ExampleOutput.WriteLine();

            var defaultConvention = new DefaultTopicConvention();
            var attrConvention = new AttributeTopicConvention();

            ExampleOutput.WriteInfo("Default (type)", defaultConvention.GetTopic(typeof(OrderPlaced)));
            ExampleOutput.WriteInfo("Default (instance)", defaultConvention.GetTopic(new OrderPlaced(Guid.NewGuid(), 1m)));
            ExampleOutput.WriteInfo("Attribute", attrConvention.GetTopic(typeof(LowStockAlert)));
            ExampleOutput.WriteInfo("Fallback", attrConvention.GetTopic(typeof(OrderPlaced)));

            ExampleOutput.WriteLine();
            ExampleOutput.WriteSuccess("Topic naming maps event types to queue destinations.");
        }

        /// <summary>
        /// Distributed event bus over InMemory message queue.
        /// </summary>
        public static async Task RunDistributedExample()
        {
            ExampleOutput.WriteHeader("Distributed Event Bus (InMemory transport)");
            ExampleOutput.WriteLine();

            using var queue = new InMemoryMessageQueue();
            using var bus = new DistributedEventBus(queue);

            var handler = new OrderPlacedHandler();
            bus.Subscribe(handler);
            await bus.SubscribeToTransportAsync<OrderPlaced>();

            ExampleOutput.WriteInfo("Transport", "InMemory message queue");
            ExampleOutput.WriteInfo("Topic", new DefaultTopicConvention().GetTopic(typeof(OrderPlaced)));
            ExampleOutput.WriteLine();

            await bus.PublishAsync(new OrderPlaced(Guid.NewGuid(), 200m) { CorrelationId = Guid.NewGuid() });

            // InMemory queue dispatches async — wait briefly
            await Task.Delay(100);

            ExampleOutput.WriteLine();
            ExampleOutput.WriteSuccess("Event serialized → InMemory queue → deserialized → handler.");
        }

        /// <summary>
        /// Transactional outbox — events persisted, then published by processor.
        /// </summary>
        public static async Task RunOutboxExample()
        {
            ExampleOutput.WriteHeader("Transactional Outbox");
            ExampleOutput.WriteLine();

            var outboxStore = new InMemoryOutboxStore();
            var handler = new OrderPlacedHandler();
            using var innerBus = new InProcessEventBus();
            innerBus.Subscribe(handler);

            using var outboxBus = new OutboxEventBus(innerBus, outboxStore);

            // Step 1: Publish (goes to outbox, not handler)
            var evt = new OrderPlaced(Guid.NewGuid(), 150m);
            await outboxBus.PublishAsync(evt);

            var pending = await outboxStore.GetPendingAsync(10);
            ExampleOutput.WriteInfo("After publish", $"{pending.Count} pending entry in outbox");
            ExampleOutput.WriteInfo("Handler called?", "No — event is in outbox only");
            ExampleOutput.WriteLine();

            // Step 2: Process outbox
            var processor = new OutboxProcessor(outboxStore, innerBus);
            var processed = await processor.ProcessBatchAsync();

            ExampleOutput.WriteInfo("Processed", $"{processed} entry");
            ExampleOutput.WriteLine();

            var published = outboxStore.GetAll();
            ExampleOutput.WriteInfo("Outbox status", published[0].Status.ToString());

            ExampleOutput.WriteLine();
            ExampleOutput.WriteSuccess("Event persisted → outbox processor → handler. At-least-once delivery.");
        }
    }

    // ── Internal helpers ───────────────────────────

    internal class CountingHandler : IEventHandler<OrderPlaced>
    {
        private readonly Action _onHandle;
        public CountingHandler(Action onHandle) => _onHandle = onHandle;
        public Task HandleAsync(OrderPlaced @event, EventContext context, CancellationToken ct = default)
        {
            _onHandle();
            return Task.CompletedTask;
        }
    }
}
