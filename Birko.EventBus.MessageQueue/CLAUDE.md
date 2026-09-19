# Birko.EventBus.MessageQueue

## Overview
Distributed event bus backed by Birko.MessageQueue. Bridges strongly-typed IEventBus with transport-agnostic IMessageQueue.

## Project Location
- **Directory:** `Birko.EventBus.MessageQueue/`
- **Type:** Shared Project (.shproj / .projitems)
- **Namespace:** `Birko.EventBus.MessageQueue`

## Components

| File | Description |
|------|-------------|
| DistributedEventBus.cs | IEventBus over IMessageQueue — serializes events as EventEnvelope, dispatches received envelopes to handlers |
| DistributedEventBusOptions.cs | TopicConvention, Serializer, RetryPolicy, DeadLetterOptions, ConsumerOptions, AutoSubscribe |
| EventEnvelope.cs | Transport wrapper: EventId, EventType (assembly-qualified), Source, Payload (JSON), Headers, TenantGuid |
| AutoSubscriber.cs | Scans DI for IEventHandler&lt;T&gt;, calls SubscribeToTransportAsync for each discovered event type |
| DistributedEventBusHostedService.cs | IHostedService that runs AutoSubscriber on startup |
| DistributedEventBusServiceCollectionExtensions.cs | AddDistributedEventBus() DI extension |

## Architecture

```
PublishAsync<T>(event)
  → Enrichers → Build EventEnvelope → Serialize → IMessageQueue.Producer.SendAsync(topic, message)

InMemoryChannel / MQTT / RabbitMQ delivers message to consumer

Consumer callback:
  → Deserialize EventEnvelope → Type.GetType(EventType) → Deserialize Payload
  → Pipeline → Dispatch to DI + manual handlers
```

## Important Notes
- **Topic routing:** Both publish and subscribe use `ITopicConvention.GetTopic(Type)` (type-based) for consistent routing
- **Type resolution:** EventType is stored as AssemblyQualifiedName — consumer must have the event type's assembly loaded
- **Subscribe is not enough to receive (CR-L256):** `Subscribe<T>` only registers a handler into the manual map; handlers are read solely inside the `SubscribeToTransportAsync<T>` delivery callback. A caller who only calls `Subscribe` and never establishes a transport subscription silently receives nothing. Call `SubscribeToTransportAsync<T>` for each event type, or use `AutoSubscriber`/`DistributedEventBusHostedService` (AutoSubscribe) to wire them from DI on startup. `Subscribe` does not auto-wire the transport by design — it is sync and the transport subscribe is network-bound async (CR-M188 removed sync-over-async here).
- **Error isolation (CR-H114):** handler exceptions are caught per-handler so one failure doesn't stop the others, but they are then re-thrown after the loop (single exception, or an `AggregateException`) so the delivery callback faults and the transport drives retry / dead-letter. Swallowing them would ack-and-lose failed messages.
- The InMemoryChannel dispatches in a background Task.Run — tests need polling/delay for assertions

## Dependencies
- Birko.EventBus — Core interfaces
- Birko.MessageQueue — Transport abstraction
- Microsoft.Extensions.DependencyInjection.Abstractions
- Microsoft.Extensions.Hosting.Abstractions (IHostedService)

## Maintenance
- When adding new files, update the .projitems file
- If adding new transport features, update the EventEnvelope model
