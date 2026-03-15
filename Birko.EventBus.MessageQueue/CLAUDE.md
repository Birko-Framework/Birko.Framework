# Birko.EventBus.MessageQueue

## Overview
Distributed event bus backed by Birko.MessageQueue. Bridges strongly-typed IEventBus with transport-agnostic IMessageQueue.

## Project Location
- **Directory:** `C:\Source\Birko.EventBus.MessageQueue\`
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
- **Error isolation:** Handler exceptions are caught in the consumer callback — one handler failure doesn't affect others
- The InMemoryChannel dispatches in a background Task.Run — tests need polling/delay for assertions

## Dependencies
- Birko.EventBus — Core interfaces
- Birko.MessageQueue — Transport abstraction
- Microsoft.Extensions.DependencyInjection.Abstractions
- Microsoft.Extensions.Hosting.Abstractions (IHostedService)

## Maintenance
- When adding new files, update the .projitems file
- If adding new transport features, update the EventEnvelope model
