# Birko.EventBus

## Overview
Strongly-typed event bus for modular monolith architectures. In-process pub/sub with pipeline behaviors, enrichment, deduplication, and DI integration.

## Project Location
- **Directory:** `C:\Source\Birko.EventBus\`
- **Type:** Shared Project (.shproj / .projitems)
- **Namespace:** `Birko.EventBus`

## Components

| File | Description |
|------|-------------|
| Core/IEvent.cs | Marker interface (EventId, OccurredAt, Source) |
| Core/EventBase.cs | Abstract record base class (adds CorrelationId) |
| Core/EventContext.cs | Handler context (EventId, Source, CorrelationId, TenantId, DeliveryCount, Metadata) |
| Core/IEventHandler.cs | IEventHandler&lt;TEvent&gt; — HandleAsync(event, context, ct) |
| Core/IEventBus.cs | PublishAsync&lt;T&gt;, Subscribe&lt;T&gt;, IDisposable |
| Core/IEventSubscription.cs | Subscription handle (Dispose to unsubscribe) |
| Local/InProcessEventBus.cs | In-memory bus — DI + manual handler resolution, sequential/parallel dispatch |
| Local/InProcessEventBusOptions.cs | MaxConcurrency, ErrorHandlingMode (Continue/Stop) |
| Pipeline/IEventPipelineBehavior.cs | Middleware interface — HandleAsync(event, context, next, ct) |
| Pipeline/EventPipeline.cs | Ordered pipeline executor (Russian doll pattern) |
| Routing/ITopicConvention.cs | Event type → topic name strategy |
| Routing/TopicAttribute.cs | [Topic("custom.topic")] attribute |
| Routing/DefaultTopicConvention.cs | Kebab-case convention: "events.order-placed" |
| Routing/AttributeTopicConvention.cs | Attribute-based with fallback to default |
| Enrichment/IEventEnricher.cs | Pre-publish enrichment interface |
| Enrichment/CorrelationEventEnricher.cs | Ensures CorrelationId is set |
| Deduplication/IDeduplicationStore.cs | Check/record processed event IDs |
| Deduplication/InMemoryDeduplicationStore.cs | ConcurrentDictionary with TTL cleanup |
| Deduplication/DeduplicationBehavior.cs | Pipeline behavior that skips duplicates |
| Extensions/EventBusServiceCollectionExtensions.cs | DI: AddEventBus(), AddEventHandler&lt;T,H&gt;(), AddEventPipelineBehavior&lt;T&gt;(), AddEventEnricher&lt;T&gt;(), AddEventDeduplication() |

## Architecture

```
PublishAsync<T>(event)
  → Enrichers (CorrelationId, TenantId, custom)
  → Pipeline (behaviors in registration order, Russian doll)
    → Dispatch to handlers (DI-resolved + manual subscriptions)
      Sequential (MaxConcurrency=1) or Parallel (semaphore)
      Error isolation per handler (Continue mode)
```

## Dependencies
- **Microsoft.Extensions.DependencyInjection.Abstractions** — DI registration (provided by consuming project)

## Important Notes
- Consuming project must reference `Microsoft.Extensions.DependencyInjection.Abstractions` (or use ASP.NET Core which includes it)
- EventContext uses mutable setters for CorrelationId/TenantId/Metadata so enrichers can modify them
- EventBase is an abstract record — concrete events should be `sealed record`
- ITopicConvention is used by Birko.EventBus.MessageQueue (not this project) for distributed routing

## Maintenance
- When adding new files, update the .projitems file
- Pipeline behaviors execute in registration order — document this for consumers
