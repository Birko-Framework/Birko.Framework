---
area: event-bus-and-messaging
generated-at: f3ac6755e788bc3e4693d27d37c583d67532a816
generated-on: 2026-07-30
sources:
  - ../Birko.EventBus.EventSourcing/DomainEventPublished.cs
  - ../Birko.EventBus.EventSourcing/EventReplayService.cs
  - ../Birko.EventBus.EventSourcing/EventStoreEventBus.cs
  - ../Birko.EventBus.MessageQueue/AutoSubscriber.cs
  - ../Birko.EventBus.MessageQueue/DistributedEventBus.cs
  - ../Birko.EventBus.MessageQueue/DistributedEventBusHostedService.cs
  - ../Birko.EventBus.MessageQueue/DistributedEventBusOptions.cs
  - ../Birko.EventBus.MessageQueue/DistributedEventBusServiceCollectionExtensions.cs
  - ../Birko.EventBus.MessageQueue/EventEnvelope.cs
  - ../Birko.EventBus.Outbox/Core/IOutboxStore.cs
  - ../Birko.EventBus.Outbox/Core/OutboxEntry.cs
  - ../Birko.EventBus.Outbox/Core/OutboxOptions.cs
  - ../Birko.EventBus.Outbox/Core/OutboxStatus.cs
  - ../Birko.EventBus.Outbox/Extensions/OutboxServiceCollectionExtensions.cs
  - ../Birko.EventBus.Outbox/Hosting/OutboxProcessorHostedService.cs
  - ../Birko.EventBus.Outbox/Publishing/OutboxEventBus.cs
  - ../Birko.EventBus.Outbox/Publishing/OutboxProcessor.cs
  - ../Birko.EventBus.Outbox/Stores/InMemoryOutboxStore.cs
  - ../Birko.EventBus/Core/EventBase.cs
  - ../Birko.EventBus/Core/EventContext.cs
  - ../Birko.EventBus/Core/IEvent.cs
  - ../Birko.EventBus/Core/IEventBus.cs
  - ../Birko.EventBus/Core/IEventHandler.cs
  - ../Birko.EventBus/Core/IEventScopeAccessor.cs
  - ../Birko.EventBus/Core/IEventSubscription.cs
  - ../Birko.EventBus/Deduplication/DeduplicationBehavior.cs
  - ../Birko.EventBus/Deduplication/IDeduplicationStore.cs
  - ../Birko.EventBus/Deduplication/InMemoryDeduplicationStore.cs
  - ../Birko.EventBus/Enrichment/CorrelationEventEnricher.cs
  - ../Birko.EventBus/Enrichment/IEventEnricher.cs
  - ../Birko.EventBus/Extensions/EventBusServiceCollectionExtensions.cs
  - ../Birko.EventBus/Local/InProcessEventBus.cs
  - ../Birko.EventBus/Local/InProcessEventBusOptions.cs
  - ../Birko.EventBus/Pipeline/EventPipeline.cs
  - ../Birko.EventBus/Pipeline/IEventPipelineBehavior.cs
  - ../Birko.EventBus/Pipeline/RuleFilterBehavior.cs
  - ../Birko.EventBus/Routing/AttributeTopicConvention.cs
  - ../Birko.EventBus/Routing/DefaultTopicConvention.cs
  - ../Birko.EventBus/Routing/ITopicConvention.cs
  - ../Birko.EventBus/Routing/TopicAttribute.cs
  - ../Birko.MessageQueue.InMemory/InMemoryChannel.cs
  - ../Birko.MessageQueue.InMemory/InMemoryConsumer.cs
  - ../Birko.MessageQueue.InMemory/InMemoryMessageQueue.cs
  - ../Birko.MessageQueue.InMemory/InMemoryMessageQueueOptions.cs
  - ../Birko.MessageQueue.InMemory/InMemoryProducer.cs
  - ../Birko.MessageQueue.InMemory/InMemorySubscription.cs
  - ../Birko.MessageQueue.MQTT/MqttConsumer.cs
  - ../Birko.MessageQueue.MQTT/MqttLastWill.cs
  - ../Birko.MessageQueue.MQTT/MqttMessageQueue.cs
  - ../Birko.MessageQueue.MQTT/MqttProducer.cs
  - ../Birko.MessageQueue.MQTT/MqttQualityOfService.cs
  - ../Birko.MessageQueue.MQTT/MqttSettings.cs
  - ../Birko.MessageQueue.MQTT/MqttSubscription.cs
  - ../Birko.MessageQueue.MQTT/MqttTopic.cs
  - ../Birko.MessageQueue.Redis/RedisConsumer.cs
  - ../Birko.MessageQueue.Redis/RedisProducer.cs
  - ../Birko.MessageQueue.Redis/RedisStreamQueue.cs
  - ../Birko.MessageQueue.Redis/RedisStreamSettings.cs
  - ../Birko.MessageQueue.Redis/RedisSubscription.cs
  - ../Birko.MessageQueue/Core/ConsumerOptions.cs
  - ../Birko.MessageQueue/Core/IMessageConsumer.cs
  - ../Birko.MessageQueue/Core/IMessageHandler.cs
  - ../Birko.MessageQueue/Core/IMessageProducer.cs
  - ../Birko.MessageQueue/Core/IMessageQueue.cs
  - ../Birko.MessageQueue/Core/ISubscription.cs
  - ../Birko.MessageQueue/Core/MessageAckMode.cs
  - ../Birko.MessageQueue/Core/MessageContext.cs
  - ../Birko.MessageQueue/Core/MessageFingerprint.cs
  - ../Birko.MessageQueue/Core/MessageHeaders.cs
  - ../Birko.MessageQueue/Core/QueueMessage.cs
  - ../Birko.MessageQueue/Patterns/IPublisher.cs
  - ../Birko.MessageQueue/Patterns/IReceiver.cs
  - ../Birko.MessageQueue/Patterns/ISender.cs
  - ../Birko.MessageQueue/Patterns/ISubscriber.cs
  - ../Birko.MessageQueue/Retry/DeadLetterOptions.cs
  - ../Birko.MessageQueue/Retry/RetryPolicy.cs
  - ../Birko.MessageQueue/Serialization/EncryptingMessageSerializer.cs
  - ../Birko.MessageQueue/Serialization/IMessageSerializer.cs
  - ../Birko.MessageQueue/Serialization/JsonMessageSerializer.cs
  - ../Birko.MessageQueue/Transactions/ITransactionalProducer.cs
shaped-by: []
---

# Event publication, handlers, outbox and message-queue transport

## Purpose

This capability is how one part of a Birko application tells other parts that something happened,
without the two parts knowing about each other. A publisher raises a strongly-typed *event*
(`IEvent`); zero or more *handlers* (`IEventHandler<TEvent>`) receive it. Three interchangeable
`IEventBus` implementations decide *where* delivery happens: `InProcessEventBus` runs handlers in the
publishing call (modular monolith), `DistributedEventBus` serialises the event onto a message-queue
topic so another process receives it, and `OutboxEventBus` writes it to a database table so it is
persisted atomically with the business data and published later by a background processor.

Underneath the distributed bus sits a transport abstraction of its own — `Birko.MessageQueue`
(`IMessageQueue` = a `Producer` + a `Consumer`) — with three concrete backends in this area:
in-memory `System.Threading.Channels`, MQTT (MQTTnet), and Redis Streams (StackExchange.Redis). Those
three backends make materially different delivery guarantees, and the differences are load-bearing:
whether a failing handler causes redelivery, whether `Delay`/`TimeToLive` are honoured, and whether
two subscribers on the same destination both receive a message all depend on which backend is
plugged in. Consumers of this capability are anything that needs decoupled notification —
event-sourced projections (`EventStoreEventBus`, `EventReplayService`), tenant enrichment, and
application modules reacting to each other's domain events.

## Requirements

### Requirement: Event identity and timestamp defaults

The system SHALL give every `EventBase`-derived event a fresh `EventId` (`Guid.NewGuid()`) and an
`OccurredAt` read from the static, replaceable `EventBase.DefaultClock` at construction, and SHALL
require each concrete event to supply `Source` via an abstract override.

#### Scenario: Two events constructed in sequence

- **Given** a sealed record deriving from `EventBase`
- **When** two instances are constructed
- **Then** their `EventId` values differ, and each `OccurredAt` equals the value
  `EventBase.DefaultClock.UtcNow` returned at construction time

#### Scenario: Test clock substituted

- **Given** `EventBase.DefaultClock` is assigned a provider returning a fixed `UtcNow`
- **When** a new event is constructed without an explicit `OccurredAt`
- **Then** `OccurredAt` equals that fixed value (the assignment is process-global static state)

#### Scenario: Caller overrides the defaults

- **Given** an event type with `init` accessors inherited from `EventBase`
- **When** it is constructed with explicit `EventId` and `OccurredAt` values
- **Then** the supplied values are kept and no new Guid or clock read replaces them

### Requirement: EventContext construction from an event

The system SHALL build an `EventContext` from an `IEvent` via `EventContext.From`, copying `EventId`
and `Source`, copying `CorrelationId` **only when the event is an `EventBase`** (otherwise null),
defaulting `DeliveryCount` to 1 and `Metadata` to an empty dictionary when none is supplied.

#### Scenario: Event derives from EventBase with a correlation id

- **Given** an `EventBase` event whose `CorrelationId` is set
- **When** `EventContext.From(@event)` is called
- **Then** `context.CorrelationId` equals the event's `CorrelationId`

#### Scenario: Event implements IEvent directly

- **Given** a type that implements `IEvent` without deriving from `EventBase`, even if it declares its
  own `CorrelationId` property
- **When** `EventContext.From(@event)` is called
- **Then** `context.CorrelationId` is null

#### Scenario: Tenant is never inferred

- **Given** any event
- **When** `EventContext.From(@event)` is called without the `tenantGuid` argument
- **Then** `context.TenantGuid` is null — tenant is only ever populated by an `IEventEnricher` or by
  an explicit caller argument

### Requirement: In-process publication dispatches to DI handlers plus manual subscriptions

The system SHALL, on `InProcessEventBus.PublishAsync<TEvent>`, throw `ObjectDisposedException` if the
bus is disposed, run every registered `IEventEnricher` in order, then collect handlers as
`IEnumerable<IEventHandler<TEvent>>` resolved from the supplied `IServiceProvider` followed by
manually `Subscribe`d handlers, and dispatch to them.

#### Scenario: Both registration styles present

- **Given** one `IEventHandler<OrderPlaced>` registered in DI and one added via `Subscribe`
- **When** `PublishAsync(new OrderPlaced(...))` is called
- **Then** both handlers are invoked, DI-resolved handlers first

#### Scenario: No service provider supplied

- **Given** an `InProcessEventBus` constructed with `serviceProvider: null`
- **When** an event is published
- **Then** only manually subscribed handlers are invoked and no resolution is attempted

#### Scenario: Published after disposal

- **Given** an `InProcessEventBus` on which `Dispose()` has been called
- **When** `PublishAsync` is called
- **Then** `ObjectDisposedException` is thrown

#### Scenario: Handler resolution is exact-type only

- **Given** a handler registered as `IEventHandler<EventBase>` and an event of a derived type
- **When** that derived event is published
- **Then** the handler is not invoked — resolution uses the closed generic `IEventHandler<TEvent>` for
  the static `TEvent` of the publish call, and manual subscriptions are keyed by `typeof(TEvent)`

### Requirement: In-process publication skips the pipeline when there are no handlers

The system SHALL return from `InProcessEventBus.PublishAsync` immediately after enrichment when the
collected handler list is empty, so no `IEventPipelineBehavior` runs for that publish.

#### Scenario: Deduplication behaviour registered but no handler for the event type

- **Given** `AddEventDeduplication()` is registered and no handler exists for `OrderPlaced`
- **When** `OrderPlaced` with a given `EventId` is published
- **Then** `DeduplicationBehavior` is not invoked and the `EventId` is **not** recorded in the
  deduplication store, so a later publish of the same `EventId` (once a handler exists) is treated as
  first-seen

#### Scenario: Enrichers still run

- **Given** a `CorrelationEventEnricher` is registered and no handler exists for the event type
- **When** the event is published
- **Then** the enricher still runs to completion before the early return

### Requirement: Pipeline behaviours wrap dispatch outermost-first

The system SHALL execute `IEventPipelineBehavior` instances as a Russian-doll chain in which the
first-registered behaviour is outermost, and SHALL invoke the handler delegate directly with no
allocation of a chain when no behaviours are registered.

#### Scenario: Two behaviours registered

- **Given** behaviours `A` then `B` registered in that order
- **When** an event is dispatched
- **Then** the call order is `A` enter → `B` enter → handlers → `B` exit → `A` exit

#### Scenario: A behaviour does not call next

- **Given** a behaviour that returns `Task.CompletedTask` without awaiting `next()`
- **When** an event is dispatched
- **Then** no inner behaviour and no handler runs, and `PublishAsync` completes successfully

#### Scenario: No behaviours registered

- **Given** an `EventPipeline` constructed from an empty (or null) behaviour sequence
- **When** `ExecuteAsync` is called
- **Then** the handler delegate is returned directly by `handler()`

### Requirement: Sequential dispatch error isolation is mode-dependent

The system SHALL, when `InProcessEventBusOptions.MaxConcurrency <= 1`, invoke handlers one at a time
and on a handler exception invoke `OnHandlerError` (when set) and then either continue to the next
handler (`ErrorHandlingMode.Continue`, the default) or rethrow and abandon the remaining handlers
(`ErrorHandlingMode.Stop`).

#### Scenario: First of three handlers throws in Continue mode

- **Given** `ErrorHandling = Continue`, `MaxConcurrency = 1`, and handlers H1 (throws), H2, H3
- **When** the event is published
- **Then** H2 and H3 still run, `PublishAsync` completes without throwing, and `OnHandlerError` was
  invoked once with the event and H1's exception

#### Scenario: First of three handlers throws in Stop mode

- **Given** `ErrorHandling = Stop`, `MaxConcurrency = 1`, and handlers H1 (throws), H2, H3
- **When** the event is published
- **Then** H2 and H3 do not run and H1's exception propagates out of `PublishAsync`

#### Scenario: No error callback configured

- **Given** `OnHandlerError` is null and `ErrorHandling = Continue`
- **When** a handler throws
- **Then** the exception is discarded with no logging of any kind and the publish reports success

### Requirement: Parallel dispatch bounds concurrency and cancels peers in Stop mode

The system SHALL, when `MaxConcurrency > 1`, launch all handler tasks eagerly gated by a
`SemaphoreSlim(MaxConcurrency)`, and in `ErrorHandlingMode.Stop` cancel a linked
`CancellationTokenSource` on the first handler failure so queued handlers abort, finally rethrowing
the first captured failure via `ExceptionDispatchInfo` after `Task.WhenAll`.

#### Scenario: Four handlers with MaxConcurrency 2

- **Given** `MaxConcurrency = 2` and four handlers
- **When** the event is published
- **Then** at most two handlers execute concurrently

#### Scenario: One handler fails in parallel Stop mode

- **Given** `MaxConcurrency = 4`, `ErrorHandling = Stop`, and one handler that throws
  `InvalidOperationException`
- **When** the event is published
- **Then** the linked token is cancelled, handlers still waiting on the semaphore observe cancellation
  and abort, follow-on `OperationCanceledException`s are swallowed, and `PublishAsync` throws the
  original `InvalidOperationException` with its stack trace preserved

#### Scenario: Caller cancels during parallel dispatch

- **Given** parallel dispatch in flight and the caller's `cancellationToken` is cancelled
- **When** a handler observes cancellation
- **Then** the `OperationCanceledException` is **not** swallowed (the filter requires
  `!cancellationToken.IsCancellationRequested`) and surfaces from `Task.WhenAll`

### Requirement: Event subscriptions are disposable and idempotent

The system SHALL return an `IEventSubscription` from `Subscribe` that reports its `EventType` and
`IsActive`, and whose `Dispose` removes the handler exactly once.

#### Scenario: In-process subscription disposed twice concurrently

- **Given** an `InProcessEventBus` subscription
- **When** `Dispose()` is called twice, possibly from two threads
- **Then** the unsubscribe action runs exactly once (guarded by `Interlocked.Exchange` on an int flag)
  and `IsActive` reports false afterwards

#### Scenario: Distributed subscription disposed concurrently

- **Given** a `DistributedEventBus` subscription
- **When** `Dispose()` is called from two threads simultaneously
- **Then** the guard is a plain non-atomic `bool` check-then-set, so both callers can pass it and the
  unsubscribe action can run twice (`List.Remove` making the second a no-op) — unlike the in-process
  subscription

#### Scenario: Bus disposed while subscriptions are live

- **Given** an `InProcessEventBus` with active subscriptions
- **When** `Dispose()` is called
- **Then** `_disposed` is set and the subscription dictionary is cleared; the previously returned
  `IEventSubscription` handles still report `IsActive == true`

### Requirement: Deduplication is mark-before-handle (at-most-once)

The system SHALL, in `DeduplicationBehavior`, atomically reserve the event's `EventId` via
`IDeduplicationStore.TryMarkProcessedAsync` **before** invoking the rest of the pipeline, and skip the
event entirely when the reservation fails. A handler failure SHALL NOT release the reservation.

#### Scenario: Same EventId published twice

- **Given** a `DeduplicationBehavior` over an `InMemoryDeduplicationStore`
- **When** the same event instance (same `EventId`) is published twice
- **Then** handlers run on the first publish only; the second returns without calling `next()`

#### Scenario: Two concurrent publishes of one EventId

- **Given** the `InMemoryDeduplicationStore` (whose `TryMarkProcessedAsync` is a
  `ConcurrentDictionary.TryAdd`)
- **When** two threads publish the same `EventId` simultaneously
- **Then** exactly one wins the `TryAdd` and runs handlers; the other is dropped

#### Scenario: Handler throws after the mark

- **Given** the event was marked and a downstream handler throws
- **When** the same event is republished later
- **Then** it is dropped as a duplicate — the failed processing is never retried through this behaviour

#### Scenario: A custom store implements only Exists + Mark

- **Given** an `IDeduplicationStore` that does not override the interface's default
  `TryMarkProcessedAsync`
- **When** two callers publish the same `EventId` concurrently
- **Then** the default composed `ExistsAsync`-then-`MarkProcessedAsync` implementation is not atomic and
  both callers may observe "not seen" and both run handlers

### Requirement: In-memory deduplication expiry is swept lazily under a claimed interval

The system SHALL retain processed `EventId`s in `InMemoryDeduplicationStore` for a TTL (default 1
hour) and SHALL sweep entries older than `now - ttl` at most once per 5-minute interval, with the
sweep slot claimed atomically via `Interlocked.CompareExchange`.

#### Scenario: Entry older than the TTL

- **Given** an `EventId` marked more than the TTL ago and at least 5 minutes since the last sweep
- **When** `ExistsAsync` or `TryMarkProcessedAsync` is next called
- **Then** the stale entry is removed and the id is treated as first-seen

#### Scenario: Sweep interval has not elapsed

- **Given** the last sweep ran 10 seconds ago
- **When** `TryMarkProcessedAsync` is called
- **Then** no sweep runs and expired-but-unswept ids are still reported as duplicates

#### Scenario: Many threads cross the interval boundary at once

- **Given** several threads calling `TryMarkProcessedAsync` just after the interval elapses
- **When** they all pass the elapsed-time check
- **Then** only the thread whose `CompareExchange` succeeds performs the sweep; the others return
  immediately

#### Scenario: MarkProcessedAsync does not sweep

- **Given** only `MarkProcessedAsync` is ever called (never `Exists`/`TryMark`)
- **When** entries age past the TTL
- **Then** no cleanup ever runs — the sweep is only triggered from `ExistsAsync` and
  `TryMarkProcessedAsync`

### Requirement: Correlation enrichment fills the context but never the event

The system SHALL, in `CorrelationEventEnricher`, leave a non-null `context.CorrelationId` untouched,
otherwise copy the event's `EventBase.CorrelationId` when it has a value, and otherwise generate a
fresh `Guid`.

#### Scenario: Event carries a correlation id

- **Given** an `EventBase` event with `CorrelationId` set and a context whose `CorrelationId` is null
- **When** the enricher runs
- **Then** the context adopts the event's value

#### Scenario: Neither event nor context carries one

- **Given** an event with a null `CorrelationId`
- **When** the enricher runs
- **Then** the context receives a newly generated `Guid` and the event itself is unmodified

#### Scenario: Context already enriched by an earlier enricher

- **Given** a preceding enricher set `context.CorrelationId`
- **When** `CorrelationEventEnricher` runs
- **Then** the existing value is preserved

### Requirement: Rule-based event filtering short-circuits non-matching events

The system SHALL, in `RuleFilterBehavior`, pass the event through unfiltered when
`RuleSet.IsEnabled` is false, otherwise evaluate the rule set against a rule context and call `next()`
only when at least one rule matched; a zero-match evaluation SHALL silently complete without invoking
handlers.

#### Scenario: Rule set disabled

- **Given** a `RuleSet` with `IsEnabled == false`
- **When** an event is published
- **Then** `next()` is invoked without any evaluation

#### Scenario: No rule matches

- **Given** an enabled rule set that matches nothing for this event
- **When** the event is published
- **Then** handlers do not run and `PublishAsync` reports success (the drop is not observable)

#### Scenario: Default context built by reflection

- **Given** no `contextFactory` was supplied
- **When** the behaviour builds its rule context
- **Then** the context contains `EventId`, `OccurredAt`, `Source`, `DeliveryCount`, plus `TenantGuid`
  and `CorrelationId` when present, then every readable public instance property of the event, then
  the context metadata — later keys added with `TryAdd`, so the fixed keys win on collision

#### Scenario: Event exposes an indexer or a throwing property getter

- **Given** an event type declaring `this[int]` and a property whose getter throws
- **When** the default rule context is built
- **Then** the indexer is skipped and the throwing property is omitted; construction succeeds

### Requirement: Topic naming for transport routing

The system SHALL derive a topic from an event type by kebab-casing the type name under the `events.`
prefix (`DefaultTopicConvention.GetTopic(Type)`), and from an event *instance* by prefixing with the
lower-cased `Source` when it is non-empty (`GetTopic(IEvent)`).

#### Scenario: Type-based topic

- **Given** an event type named `OrderPlaced`
- **When** `DefaultTopicConvention.GetTopic(typeof(OrderPlaced))` is called
- **Then** the result is `"events.order-placed"`

#### Scenario: Instance-based topic with a source

- **Given** an `OrderPlaced` instance whose `Source` is `"Sales"`
- **When** `DefaultTopicConvention.GetTopic(@event)` is called
- **Then** the result is `"sales.order-placed"` — a different topic from the type-based form

#### Scenario: Attribute overrides the convention

- **Given** an event class annotated `[Topic("custom.orders")]`
- **When** `AttributeTopicConvention.GetTopic` is called with either the type or an instance
- **Then** the result is `"custom.orders"`, cached per type in a `ConcurrentDictionary`

#### Scenario: Attribute-less event under the attribute convention

- **Given** an event with no `TopicAttribute` and `Source == "Sales"`
- **When** `AttributeTopicConvention.GetTopic(@event)` is called
- **Then** it delegates to `DefaultTopicConvention.GetTopic(@event)` and returns the source-prefixed
  `"sales.order-placed"`, agreeing with the default convention rather than the interface's
  type-delegating default

#### Scenario: Topic attribute constructed with null

- **Given** `new TopicAttribute(null)`
- **When** the attribute is constructed
- **Then** `ArgumentNullException` is thrown

### Requirement: Distributed publication serialises an envelope onto a type-derived topic

The system SHALL, on `DistributedEventBus.PublishAsync`, run enrichers, build an `EventEnvelope`
carrying `EventId`, the event's `AssemblyQualifiedName`, `Source`, `OccurredAt`, the context's
`CorrelationId`/`TenantGuid`/`Metadata` and the serialized event as `Payload`, and send the serialized
envelope as a `QueueMessage` to `TopicConvention.GetTopic(@event.GetType())`.

#### Scenario: Event published to the transport

- **Given** a `DistributedEventBus` over an `IMessageQueue`
- **When** `PublishAsync(new OrderPlaced(...))` is called
- **Then** one `QueueMessage` is sent to `"events.order-placed"` whose `PayloadType` is
  `typeof(EventEnvelope).AssemblyQualifiedName`, whose `Headers.CorrelationId` is the context
  correlation id as a string, and whose `Headers.ContentType` is the serializer's content type

#### Scenario: Publish-side pipeline is not executed

- **Given** an `IEventPipelineBehavior` (e.g. `DeduplicationBehavior`) registered on the distributed bus
- **When** an event is published
- **Then** the behaviour does **not** run on the publish path; it runs only inside the transport
  delivery callback established by `SubscribeToTransportAsync` — unlike `InProcessEventBus`, which
  runs the pipeline at publish time

#### Scenario: A source-prefixing convention is configured

- **Given** `DefaultTopicConvention` and an event whose `Source` is `"Sales"`
- **When** the event is published and later consumed
- **Then** both sides use the **type**-based topic `"events.order-placed"`, so
  `GetTopic(IEvent)`'s source-prefixed form is never used for routing by this bus

#### Scenario: Published after disposal

- **Given** a disposed `DistributedEventBus`
- **When** `PublishAsync`, `Subscribe` or `SubscribeToTransportAsync` is called
- **Then** `ObjectDisposedException` is thrown

### Requirement: Distributed Subscribe alone receives nothing

The system SHALL record handlers passed to `DistributedEventBus.Subscribe` in a local dictionary that
is read only by the delivery callback created by `SubscribeToTransportAsync<TEvent>`, and SHALL NOT
create a transport subscription as a side effect of `Subscribe`.

#### Scenario: Handler subscribed without a transport subscription

- **Given** `Subscribe(handler)` was called for `OrderPlaced` and `SubscribeToTransportAsync<OrderPlaced>`
  was never called
- **When** another process publishes `OrderPlaced` to the topic
- **Then** the handler is never invoked and no error is reported

#### Scenario: Transport subscription created afterwards

- **Given** the same handler, and `SubscribeToTransportAsync<OrderPlaced>()` is then awaited
- **When** an `OrderPlaced` envelope arrives on the topic
- **Then** the handler is invoked

### Requirement: Distributed delivery reconstructs the event and its context, and drops undecodable messages

The system SHALL, in the transport delivery callback, deserialize the `EventEnvelope`, resolve
`envelope.EventType` via `Type.GetType`, require it to be assignable to `TEvent`, deserialize the
payload, rebuild an `EventContext` from the envelope, and return silently (acknowledging the message)
when any of those steps yields null or an incompatible type.

#### Scenario: Well-formed envelope

- **Given** an envelope whose `EventType` resolves to `OrderPlaced` and whose payload deserializes
- **When** the message is delivered
- **Then** handlers receive the event and an `EventContext` carrying the envelope's `EventId`,
  `Source`, `CorrelationId`, `TenantGuid` and `Headers`

#### Scenario: Event type not present in the consuming process

- **Given** an envelope whose `EventType` assembly-qualified name cannot be resolved locally
- **When** the message is delivered
- **Then** the callback returns without throwing, so the transport treats the delivery as successful
  and the message is not retried or dead-lettered

#### Scenario: Envelope body is not an EventEnvelope

- **Given** a message whose body deserializes to null as `EventEnvelope`
- **When** the message is delivered
- **Then** the callback returns silently

#### Scenario: Delivery count header present

- **Given** any message delivered over the in-memory, MQTT or Redis transport
- **When** the message is delivered
- **Then** `context.DeliveryCount` is 1 — the callback reads the custom header `x-delivery-count` (its
  parsed value when present, 1 when missing or unparseable), but no producer or consumer in the
  framework ever writes that key, so a handler cannot distinguish a redelivery from a first attempt
  even on Redis where redelivery genuinely happens

#### Scenario: No handler registered for the event type

- **Given** a transport subscription exists but no DI or manual handler for `TEvent`
- **When** a message arrives
- **Then** the callback returns before running the pipeline

### Requirement: Distributed delivery runs every handler then faults for transport retry

The system SHALL invoke all handlers for a delivered event even when earlier handlers throw,
collecting their exceptions, and SHALL then rethrow the single exception (or an `AggregateException`
of several) so the delivery callback faults and the transport can apply its own retry / dead-letter
policy.

#### Scenario: One of three handlers throws

- **Given** handlers H1 (throws), H2, H3 subscribed for a delivered event
- **When** the message is delivered
- **Then** H2 and H3 still run and the callback ultimately throws H1's exception

#### Scenario: Two handlers throw

- **Given** two failing handlers
- **When** the message is delivered
- **Then** the callback throws an `AggregateException` wrapping both

#### Scenario: Delivery over the in-memory transport

- **Given** the same failing handler but an `InMemoryMessageQueue` as transport
- **When** the message is delivered
- **Then** `InMemoryChannel`'s dispatch loop catches and discards the fault, so **no** redelivery or
  dead-lettering occurs — the deliberate rethrow has no observable effect on this backend

#### Scenario: Retry options set on the bus

- **Given** `DistributedEventBusOptions.RetryPolicy` and `DeadLetterOptions` are configured
- **When** a delivery fails
- **Then** `DistributedEventBus` never reads those properties; only transport-level configuration
  (`ConsumerOptions`, provider settings) affects retry and dead-lettering

### Requirement: Distributed bus disposal unsubscribes synchronously or asynchronously

The system SHALL dispose each transport `ISubscription` synchronously in `Dispose()` and await
`UnsubscribeAsync` on each in `DisposeAsync()`, clearing the manual-handler dictionary in both cases,
and SHALL make both paths no-ops after the first.

#### Scenario: Synchronous disposal

- **Given** a bus with two transport subscriptions
- **When** `Dispose()` is called
- **Then** each subscription's `Dispose()` is invoked (no blocking wait on an async unsubscribe)

#### Scenario: Asynchronous disposal

- **Given** the same bus
- **When** `await using` scope ends and `DisposeAsync()` runs
- **Then** each subscription's `UnsubscribeAsync()` is awaited in turn

#### Scenario: Disposed twice

- **Given** a bus already disposed
- **When** `Dispose()` or `DisposeAsync()` is called again
- **Then** it returns immediately without touching subscriptions

### Requirement: Automatic transport subscription from DI-registered handlers

The system SHALL, in `AutoSubscriber.SubscribeAllAsync`, scan every assembly in the current
`AppDomain` for non-abstract, non-interface types implementing `IEventHandler<T>`, keep those `T` that
are classes assignable to `IEvent` **and** for which DI resolves at least one handler instance, and
call `DistributedEventBus.SubscribeToTransportAsync<T>` reflectively for each distinct `T`.

#### Scenario: Handler type exists but is not registered in DI

- **Given** a class implementing `IEventHandler<OrderPlaced>` that was never added to the service
  collection
- **When** `SubscribeAllAsync` runs
- **Then** no transport subscription is created for `OrderPlaced`

#### Scenario: Assembly fails to load all its types

- **Given** an assembly whose `GetTypes()` throws `ReflectionTypeLoadException`
- **When** the scan reaches it
- **Then** the non-null types from `ex.Types` are used and the scan continues

#### Scenario: Two handlers for the same event type

- **Given** two registered handlers for `OrderPlaced`
- **When** `SubscribeAllAsync` runs
- **Then** exactly one transport subscription is created (event types are collected in a `HashSet`)

#### Scenario: Event type is a struct

- **Given** a handler closed over a value-type `IEvent`
- **When** the discovery filter runs
- **Then** it is skipped (`eventType.IsClass` is false) rather than throwing from
  `MakeGenericMethod`

### Requirement: Hosted auto-subscription requires the bus to be a DistributedEventBus

The system SHALL construct `DistributedEventBusHostedService` only when the resolved `IEventBus` is a
`DistributedEventBus`, throwing `InvalidOperationException` otherwise, and SHALL run the auto
subscriber on `StartAsync` only when `DistributedEventBusOptions.AutoSubscribe` is true.

#### Scenario: Distributed bus registered alone

- **Given** `AddDistributedEventBus()` with `AutoSubscribe` left at its default true
- **When** the host starts
- **Then** the hosted service is registered and `AutoSubscriber.SubscribeAllAsync` runs

#### Scenario: AutoSubscribe disabled

- **Given** `AddDistributedEventBus(o => o.AutoSubscribe = false)`
- **When** the host starts
- **Then** no `IHostedService` was registered at all, and even if constructed, `StartAsync` returns
  immediately

#### Scenario: Outbox decorator applied over the distributed bus

- **Given** `AddDistributedEventBus()` followed by `AddOutboxEventBus()`, so `IEventBus` resolves to an
  `OutboxEventBus`
- **When** the hosted service is constructed at host startup
- **Then** the `as DistributedEventBus` cast yields null and `InvalidOperationException` is thrown,
  naming the actual type

### Requirement: Outbox publication persists instead of dispatching

The system SHALL, in `OutboxEventBus.PublishAsync`, run enrichers and then save an `OutboxEntry`
(new `Id`, the event's `EventId`, assembly-qualified `EventType`, serialized `Payload`, `Source`,
context `CorrelationId`/`TenantGuid`, copied `Headers`, `Status = Pending`) to the `IOutboxStore`
without invoking the inner bus, and SHALL delegate `Subscribe` to the inner bus.

#### Scenario: Event published through the outbox decorator

- **Given** an `OutboxEventBus` wrapping a `DistributedEventBus`
- **When** `PublishAsync(@event)` is called
- **Then** exactly one `Pending` entry is saved and nothing is sent to the transport during the call

#### Scenario: Store write fails

- **Given** an `IOutboxStore.SaveAsync` that throws
- **When** `PublishAsync` is called
- **Then** the exception propagates to the caller (so the surrounding business transaction can roll
  back) and no event is published

#### Scenario: Subscribing through the decorator

- **Given** an `OutboxEventBus`
- **When** `Subscribe(handler)` is called
- **Then** the call is forwarded to the inner bus and its subscription handle is returned

#### Scenario: Decorator disposal

- **Given** an `OutboxEventBus`
- **When** `Dispose()` is called
- **Then** the inner bus is disposed as well, once

### Requirement: Outbox batch processing publishes, marks, and never aborts the batch on one bad entry

The system SHALL, in `OutboxProcessor.ProcessBatchAsync`, fetch up to `BatchSize` pending entries,
and for each: mark it failed when its `EventType` cannot be resolved or its payload cannot be
deserialized to `IEvent`, otherwise publish it through the inner bus (reflectively, as the concrete
event type) inside the scope reconstructed by `IEventScopeAccessor` and mark it published; any
exception SHALL be unwrapped from `TargetInvocationException` and recorded via `MarkFailedAsync` with
the configured `MaxAttempts`. Every entry — succeeded or failed — SHALL count toward the returned
processed count.

#### Scenario: Batch of three, one unresolvable type

- **Given** three pending entries, one whose `EventType` no longer resolves
- **When** `ProcessBatchAsync` runs
- **Then** two are `Published`, the third records `"Cannot resolve type: …"` in `LastError` with
  `Attempts` incremented, and the method returns 3

#### Scenario: Inner bus throws synchronously

- **Given** an inner bus whose `PublishAsync` throws `ObjectDisposedException` before its first await
- **When** the entry is processed
- **Then** the reflection wrapper is unwrapped and `LastError` is the `ObjectDisposedException`
  message, not `"Exception has been thrown by the target of an invocation."`

#### Scenario: Cancellation between entries

- **Given** a cancelled token and a claimed batch
- **When** the loop reaches the next entry's `ThrowIfCancellationRequested`
- **Then** `OperationCanceledException` propagates and the remaining claimed entries stay in
  `Publishing` until a stale-claim reclaim returns them to `Pending`

#### Scenario: Empty outbox

- **Given** no pending entries
- **When** `ProcessBatchAsync` runs
- **Then** it returns 0 without publishing anything

### Requirement: Outbox scope restoration is opt-in and no-op by default

The system SHALL build a scope context from each entry (`EventContext.From` with the entry's
`TenantGuid` and `Headers`, plus the entry's `CorrelationId`) and run the publish inside
`IEventScopeAccessor.RunWithScopeAsync`, defaulting to `NullEventScopeAccessor` which invokes the body
with no scope established.

#### Scenario: No scope bridge registered

- **Given** `AddOutbox<TStore>()` with no `IEventScopeAccessor` in DI
- **When** the processor publishes an entry
- **Then** `NullEventScopeAccessor` runs the publish directly and handlers observe whatever ambient
  scope the background loop happens to have (typically none)

#### Scenario: Scope bridge registered

- **Given** an `IEventScopeAccessor` registered in DI
- **When** `AddOutbox<TStore>()` builds the processor
- **Then** the accessor is passed in and receives the entry's persisted `TenantGuid` and
  `CorrelationId` on every publish

#### Scenario: Message-queue delivery path

- **Given** a `DistributedEventBus` transport subscription and a registered `IEventScopeAccessor`
- **When** a message is delivered and handlers run
- **Then** no scope is established — `DistributedEventBus` does not consume `IEventScopeAccessor` at
  all, so scope restoration applies to the outbox path only

### Requirement: Outbox retention cleanup is throttled

The system SHALL delete `Published`/`Failed` entries older than `now - RetentionPeriod` in
`CleanupAsync`, and SHALL run that prune from `CleanupIfDueAsync` only when at least
`CleanupInterval` has elapsed since the last successful prune (the first call always runs).

#### Scenario: First poll after startup

- **Given** a freshly constructed processor (`_lastCleanupUtc == DateTime.MinValue`)
- **When** `CleanupIfDueAsync` is called
- **Then** the prune runs and the method returns true

#### Scenario: Second poll five seconds later

- **Given** `CleanupInterval` at its 1-hour default and a prune that just ran
- **When** `CleanupIfDueAsync` is called again
- **Then** no prune runs and the method returns false

#### Scenario: Retention measured from creation

- **Given** an entry created 10 days ago and published 1 minute ago, with a 7-day retention
- **When** `InMemoryOutboxStore.CleanupAsync` runs
- **Then** the entry is deleted — the cutoff is compared against `CreatedAt`, not `PublishedAt`

### Requirement: Outbox background loop survives failures and stops on cancellation

The system SHALL, in `OutboxProcessorHostedService`, loop until the stopping token is cancelled,
calling `ProcessBatchAsync` then `CleanupIfDueAsync`, swallowing any non-cancellation exception,
breaking out on cancellation, and delaying `PollingInterval` between iterations.

#### Scenario: Store is unreachable

- **Given** `GetPendingAsync` throws a connection exception on every call
- **When** the loop runs
- **Then** each failure is caught and discarded with no logging, and the loop keeps polling every
  `PollingInterval`

#### Scenario: Host shutdown requested

- **Given** the service is delaying between polls
- **When** the stopping token is cancelled
- **Then** the `Task.Delay` `OperationCanceledException` breaks the loop and `ExecuteAsync` returns

### Requirement: Outbox entries are claimed atomically and stale claims reclaimed

The system SHALL, in `InMemoryOutboxStore.GetPendingAsync`, under a lock: return `Publishing` entries
whose `ClaimedAt` is null or older than the stale-claim timeout (default 5 minutes) to `Pending`, then
take up to `batchSize` `Pending` entries ordered by `CreatedAt`, flipping each to `Publishing` with
`ClaimedAt = now` before returning them.

#### Scenario: Two processors poll concurrently

- **Given** ten pending entries and two processors calling `GetPendingAsync`
- **When** both calls complete
- **Then** no entry is returned to both — the claim flip happens inside the same lock as the selection

#### Scenario: Processor crashed mid-publish

- **Given** an entry left in `Publishing` with `ClaimedAt` six minutes ago
- **When** `GetPendingAsync` is next called
- **Then** the entry is reset to `Pending`, re-claimed, and published again (at-least-once delivery)

#### Scenario: Attempts reach the configured cap

- **Given** `OutboxOptions.MaxAttempts = 3` and an entry that has already failed twice
- **When** `MarkFailedAsync(entryId, error, maxAttempts: 3)` is called
- **Then** `Attempts` becomes 3, `Status` becomes `Failed`, `ClaimedAt` is cleared and the entry is
  never retried

#### Scenario: Failure below the cap

- **Given** the same entry after its first failure with `MaxAttempts = 5`
- **When** `MarkFailedAsync` runs
- **Then** `Status` returns to `Pending` so the next poll retries it

#### Scenario: Marking an unknown entry id

- **Given** an id not present in the store
- **When** `MarkPublishedAsync` or `MarkFailedAsync` is called
- **Then** the call completes successfully with no effect

### Requirement: Outbox DI wiring unwraps the decorator so the processor publishes through the real bus

The system SHALL, in `AddOutbox<TStore>`, register `OutboxOptions`, the store, an `OutboxProcessor`
whose publisher is the resolved `IEventBus` — unwrapped to `OutboxEventBus.Inner` when the resolved
bus is an `OutboxEventBus` — and an `OutboxProcessorHostedService`; and `AddOutboxEventBus` SHALL
replace the existing `IEventBus` registration with an `OutboxEventBus` wrapping it, preserving the
original lifetime.

#### Scenario: Registration order reversed

- **Given** `AddOutboxEventBus()` called before or after `AddOutbox<TStore>()`
- **When** the `OutboxProcessor` is resolved
- **Then** its publisher is the inner bus in both cases, so published entries are never written back
  into the outbox

#### Scenario: Decorating with no inner registration

- **Given** `AddOutboxEventBus()` called with no prior `IEventBus` registration
- **When** the decorator extension runs
- **Then** `InvalidOperationException` is thrown stating the inner service must be registered first

#### Scenario: Outbox store but no decorator

- **Given** `AddOutbox<TStore>()` alone
- **When** events are published through the undecorated bus
- **Then** nothing is written to the outbox and the processor finds no pending entries

#### Scenario: Multiple IEventBus registrations exist

- **Given** two `IEventBus` descriptors in the collection
- **When** the decorator looks up the inner registration
- **Then** it decorates the **last** one (the search walks the collection backwards)

### Requirement: Event-store decoration publishes appended domain events

The system SHALL, in `EventStoreEventBus`, delegate all read operations to the inner
`IAsyncEventStore` unchanged, and after a successful `AppendAsync` / `AppendRangeAsync` publish one
`DomainEventPublished` per appended domain event through the event bus, in append order.

#### Scenario: Single append

- **Given** an `EventStoreEventBus` over an inner store and a bus
- **When** `AppendAsync(domainEvent)` is awaited
- **Then** the inner append completes first, then exactly one `DomainEventPublished` is published

#### Scenario: Range append

- **Given** three domain events
- **When** `AppendRangeAsync` is awaited
- **Then** the whole range is appended first, then three events are published sequentially in list
  order

#### Scenario: Publish fails after a successful append

- **Given** an event bus whose `PublishAsync` throws
- **When** `AppendAsync` is called
- **Then** the exception propagates although the domain event is already durably appended — the append
  is not rolled back

#### Scenario: Null constructor arguments

- **Given** a null inner store or null bus
- **When** `EventStoreEventBus` is constructed
- **Then** `ArgumentNullException` is thrown

### Requirement: Domain-event wrapping preserves original identity and time

The system SHALL, in the `DomainEventPublished(DomainEvent)` constructor, copy `AggregateId`,
`Version`, `EventType`, `EventData`, `Metadata` and `UserId`, and overwrite the `EventBase` defaults
with the domain event's `OccurredAt` and `EventId`; `Source` SHALL always be `"event-sourcing"`.

#### Scenario: Wrapping a historical domain event

- **Given** a domain event with `OccurredAt` two years ago and a known `EventId`
- **When** it is wrapped
- **Then** the wrapper reports that same `EventId` and `OccurredAt`, not `Guid.NewGuid()` and now

#### Scenario: Null domain event

- **Given** a null domain event
- **When** the constructor is invoked
- **Then** `ArgumentNullException` is thrown

#### Scenario: Deserialization constructor

- **Given** the parameterless constructor is used (for deserialization)
- **When** the instance is created
- **Then** `EventId`/`OccurredAt` take the `EventBase` defaults and `DomainEventType`/`EventData`
  remain null until set

### Requirement: Event replay re-publishes stored events and reports the count

The system SHALL, in `EventReplayService`, read events for an aggregate (all, from a version, or all
from a timestamp), publish each as `DomainEventPublished`, check the cancellation token before each
publish, and return the number published.

#### Scenario: Replaying an aggregate

- **Given** an aggregate with five stored events
- **When** `ReplayAggregateAsync(aggregateId)` is awaited
- **Then** five `DomainEventPublished` events are published in store order and the method returns 5

#### Scenario: Cancelled mid-replay

- **Given** a token cancelled after the second publish
- **When** the loop reaches the third iteration
- **Then** `OperationCanceledException` is thrown and the count is not returned — already-published
  events are not undone

#### Scenario: Replay combined with deduplication

- **Given** a `DeduplicationBehavior` and events already processed once
- **When** the same aggregate is replayed
- **Then** every event is dropped as a duplicate, because the wrapper preserves the original `EventId`

### Requirement: Message queue transport surface

The system SHALL expose each transport as an `IMessageQueue` providing a `Producer`, a `Consumer`, an
`IsConnected` flag, and `ConnectAsync`/`DisconnectAsync`, and SHALL dispose producer and consumer when
the queue is disposed.

#### Scenario: In-memory connect and disconnect

- **Given** a new `InMemoryMessageQueue`
- **When** `ConnectAsync` then `DisconnectAsync` are awaited
- **Then** `IsConnected` flips true then false; no real connection exists and messages flow regardless
  of the flag

#### Scenario: In-memory queue disposed

- **Given** an `InMemoryMessageQueue` with live subscriptions
- **When** `Dispose()` is called
- **Then** producer and consumer are disposed and the shared channel is torn down — every dispatch
  loop cancelled and disposed and every channel writer completed

#### Scenario: Redis connect

- **Given** a `RedisStreamQueue`
- **When** `ConnectAsync` is awaited
- **Then** the lazy connection is forced by requesting the database; `DisconnectAsync` is a no-op and
  the connection is only closed by `Dispose` when the queue owns it

#### Scenario: Redis queue built over a shared connection manager

- **Given** a `RedisStreamQueue` constructed from an existing `RedisConnectionManager`
- **When** the queue is disposed
- **Then** the connection manager is **not** disposed (ownership stays with the caller)

#### Scenario: MQTT connect with TLS and last will

- **Given** `MqttSettings` with `UseSecure`, a client certificate, and a `LastWill`
- **When** `ConnectAsync` is awaited
- **Then** the MQTTnet options carry TLS with that certificate, the will topic/payload/QoS/retain, the
  configured client id (or `birko-{guid}` when null), clean-session flag, keep-alive and timeout

### Requirement: In-memory transport fans every message out to all subscribers of a destination

The system SHALL buffer messages per destination in a bounded channel (default capacity 1000,
`FullMode.Wait`), start a single dispatch loop when the first subscriber is added and stop it when the
last leaves — both transitions taken under a per-destination lock — and deliver each drained message
to **every** currently registered subscriber, swallowing individual handler exceptions.

#### Scenario: Two subscribers on one destination

- **Given** two subscriptions on `"events.order-placed"`
- **When** one message is sent
- **Then** both handlers receive it — there is no competing-consumer / point-to-point behaviour, and
  `ConsumerOptions.GroupId` is ignored entirely

#### Scenario: Message sent before any subscriber exists

- **Given** no subscribers on a destination
- **When** a message is sent
- **Then** it is buffered in the channel and delivered once a subscriber is added

#### Scenario: Channel at capacity

- **Given** 1000 unconsumed buffered messages on a destination
- **When** another `SendAsync` is called
- **Then** the producer awaits until space frees (it does not throw or drop)

#### Scenario: A subscriber handler throws

- **Given** two subscribers where the first throws
- **When** a message is dispatched
- **Then** the exception is discarded, the second subscriber still receives the message, and the
  dispatch loop continues

#### Scenario: Pull and push mixed on one destination

- **Given** a subscriber added on a destination and a concurrent `ReadAsync` caller on the same
  destination
- **When** a message arrives
- **Then** the dispatch loop's `ReadAllAsync` and the pull read compete for it — only one observes the
  message; the two modes are mutually exclusive by contract

#### Scenario: Last subscriber removed then re-added

- **Given** the only subscription is disposed, cancelling and disposing the dispatch CTS
- **When** a new subscriber is added for that destination
- **Then** a fresh dispatch loop is started under the lock

### Requirement: In-memory delayed send is best-effort

The system SHALL, when `QueueMessage.Delay` has a value, schedule the enqueue on a detached task after
the delay and return a completed `SendAsync` immediately, observing but discarding any fault of that
detached task.

#### Scenario: Delayed message

- **Given** a message with `Delay = 2s`
- **When** `SendAsync` is awaited
- **Then** it returns before the message is enqueued, and the message appears on the destination about
  two seconds later

#### Scenario: Queue disposed during the delay

- **Given** a delayed message pending and the queue is disposed (channel writers completed)
- **When** the delay elapses
- **Then** the write fails, the fault is observed and discarded, and the caller — whose `SendAsync`
  already returned success — is never informed; the message is lost

#### Scenario: Non-delayed send

- **Given** a message with no `Delay`
- **When** `SendAsync` is awaited
- **Then** the write to the channel is awaited inline and failures propagate to the caller

### Requirement: Acknowledgment semantics differ per transport

The system SHALL implement `ConsumerOptions.AckMode` per backend: the in-memory consumer tracks
manual-ack messages in a local dictionary keyed by message id; the MQTT consumer implements
`AcknowledgeAsync` and `RejectAsync` as no-ops (protocol-level QoS only); the Redis consumer performs
`XACK` and only when a consumer group is in use.

#### Scenario: In-memory manual ack

- **Given** an in-memory subscription with `AckMode = ManualAck`
- **When** the handler completes and `AcknowledgeAsync(message.Id)` is called
- **Then** the pending entry is removed; nothing else in the transport depends on the ack

#### Scenario: In-memory manual ack, handler throws

- **Given** the same subscription and a handler that throws
- **When** the wrapped handler catches, removes the pending entry and rethrows
- **Then** a subsequent `RejectAsync(message.Id, requeue: true)` finds nothing to requeue and the
  message is lost

#### Scenario: In-memory reject with requeue after a successful handler

- **Given** a manual-ack message still tracked as pending
- **When** `RejectAsync(id, requeue: true)` is called
- **Then** the message is written back to its originating destination and redelivered

#### Scenario: In-memory reject without requeue

- **Given** a pending manual-ack message
- **When** `RejectAsync(id, requeue: false)` is called
- **Then** the tracking entry is dropped and the message is discarded

#### Scenario: MQTT acknowledge

- **Given** an MQTT subscription in any ack mode
- **When** `AcknowledgeAsync` or `RejectAsync(requeue: true)` is called
- **Then** both complete successfully and do nothing — MQTT has no application-level ack or requeue
  here

#### Scenario: Redis auto-ack in a consumer group

- **Given** a Redis subscription with a consumer group and `AckMode = AutoAck`
- **When** the handler completes successfully
- **Then** `XACK` removes the entry from the Pending Entries List

#### Scenario: Redis subscription without a consumer group

- **Given** `RedisStreamSettings.ConsumerGroup` null and no `ConsumerOptions.GroupId`
- **When** messages are consumed via `XREAD`
- **Then** no manual-ack tracking is registered and `AcknowledgeAsync`/`RejectAsync` do nothing,
  because both require a non-null `ConsumerGroup` on the tracked entry

### Requirement: Redis Streams delivery, failure retention and reclaim

The system SHALL poll a stream with `XREADGROUP` (consumer group present) or `XREAD` (otherwise),
leave a failed handler's entry unacknowledged in the Pending Entries List, and — when a consumer group
is used and `PendingRetryMilliseconds > 0` — reclaim and reprocess idle pending entries via
`XAUTOCLAIM` on each empty poll. Without a consumer group the read position is the consumer's own
`LastReadId`, which starts at `"$"` unless `FromBeginning` is set and is only ever advanced by an entry
that was actually processed.

#### Scenario: Subscription with no consumer group and default options

- **Given** `RedisStreamSettings.ConsumerGroup` null, no `ConsumerOptions.GroupId`, and
  `ConsumerOptions.FromBeginning` at its default false
- **When** the poll loop runs and messages are produced to the stream
- **Then** every `XREAD` is issued from `"$"` and returns nothing, so `LastReadId` is never assigned and
  the position stays `"$"` for the subscription's whole life — no message is ever delivered, no error is
  raised, and `RedisSubscription.IsActive` still reports true

#### Scenario: Handler fails under a consumer group

- **Given** a consumer-group subscription and a handler that throws
- **When** the entry is processed
- **Then** no `XACK` is issued, the entry remains in the PEL, and after
  `PendingRetryMilliseconds` idle time an empty poll reclaims and reprocesses it

#### Scenario: Reclaim disabled

- **Given** `PendingRetryMilliseconds = 0`
- **When** an entry fails
- **Then** it is never reclaimed during the subscription's lifetime and stays pending

#### Scenario: Subscription starting from the beginning

- **Given** `ConsumerOptions.FromBeginning = true` with a consumer group
- **When** the poll loop starts
- **Then** it first drains the consumer's own pending entries from `0-0` page by page, then switches
  the read position to `">"`

#### Scenario: New consumer group attached to a stream with retained entries

- **Given** `AutoCreateConsumerGroup = true` (the default), a stream holding retained entries, no
  existing group, and `ConsumerOptions.FromBeginning` at its default false
- **When** the group is created and the first `">"` read runs
- **Then** the whole retained stream is delivered — `EnsureConsumerGroupAsync` always creates the group
  at `"0-0"` and never consults `FromBeginning`, so the code disagrees with that option's own doc
  comment ("When false, only new messages are received")

#### Scenario: Consumer group already exists

- **Given** `AutoCreateConsumerGroup = true` and an existing group
- **When** the loop calls `StreamCreateConsumerGroupAsync`
- **Then** the `BUSYGROUP` server error (matched by message text) is swallowed and polling proceeds

#### Scenario: Message TTL exceeded

- **Given** an entry carrying `ttl_ms` and a `CreatedAt` older than that TTL, in a consumer group
- **When** the entry is processed
- **Then** the handler is not invoked and the entry is acknowledged and skipped

#### Scenario: Two subscriptions on one destination in one group

- **Given** a configured `ConsumerName` and two subscriptions to the same destination
- **When** each subscribes
- **Then** each gets a distinct consumer identity (`{ConsumerName}-{subscriptionId:N}`), so their
  pending entries and redeliveries do not collide

#### Scenario: Non-blocking poll latency

- **Given** `BlockMilliseconds = 5000` (the default) and an empty stream
- **When** a message is produced
- **Then** delivery waits for the next poll — the read is not a server-side `BLOCK`, so latency is
  bounded below by that interval (falling back to 1000 ms when the setting is null)

### Requirement: Redis poll-loop faults are surfaced and reflected in subscription liveness

The system SHALL report a non-cancellation, non-`RedisException` per-iteration fault through the
internal `PollError` event and keep polling after a 2-second backoff, retry after 2 seconds on a
`RedisException`, and on a fault that terminates the loop report it and remove the subscription so
`RedisSubscription.IsActive` becomes false.

#### Scenario: One poisoned entry

- **Given** an entry whose processing throws a serializer exception
- **When** the poll iteration faults
- **Then** `PollError` fires, the loop backs off 2 seconds and continues polling

#### Scenario: Connection lost

- **Given** the Redis server becomes unreachable, raising `RedisException`
- **When** the poll iteration fails
- **Then** the loop waits 2 seconds and retries indefinitely without raising `PollError`

#### Scenario: Consumer-group creation fails fatally

- **Given** `EnsureConsumerGroupAsync` throws a non-`BUSYGROUP` error before the loop starts
- **When** the poll task terminates
- **Then** `PollError` fires, the subscription is removed, and `IsActive` reports false rather than
  claiming an alive subscription that processes nothing

#### Scenario: Unsubscribe

- **Given** an active Redis subscription
- **When** `UnsubscribeAsync` or `Dispose` is called
- **Then** the subscription's CTS is cancelled and disposed, the registration removed, and the
  resulting `OperationCanceledException` treated as normal shutdown

### Requirement: Redis stream entries carry the whole message with a field fallback

The system SHALL write one `message` field holding the serialized `QueueMessage` (plus a `ttl_ms`
field when `TimeToLive` is set) with optional approximate `MAXLEN` trimming, and SHALL parse an entry
by preferring `message` and falling back to per-field parsing (`body`, `id`, `payload_type`,
`headers`, `created_at`, `priority`) when `message` is absent or undeserializable, returning null when
even `body` is missing.

#### Scenario: Round-trip through the Birko producer

- **Given** a `QueueMessage` with id, headers, payload type and priority
- **When** it is sent and consumed
- **Then** all of those survive, because the whole message was serialized into `message`

#### Scenario: Entry written by a foreign producer

- **Given** a stream entry with only a `body` field
- **When** it is parsed
- **Then** a `QueueMessage` with that body and default id/headers is produced

#### Scenario: Entry with neither message nor body

- **Given** an entry whose fields do not include `body`
- **When** it is parsed
- **Then** parsing returns null and the entry is skipped without invoking a handler

#### Scenario: Max stream length configured

- **Given** `MaxStreamLength = 10000`
- **When** a message is added
- **Then** `XADD` is issued with approximate `MAXLEN` trimming

#### Scenario: Empty destination

- **Given** a null or empty destination
- **When** `SendAsync` or `SubscribeAsync` is called
- **Then** `ArgumentException` is thrown naming `destination`

#### Scenario: Cancellation requested before a Redis call

- **Given** an already-cancelled token
- **When** `SendAsync`, `AcknowledgeAsync` or `RejectAsync` is called
- **Then** `OperationCanceledException` is thrown on entry, since StackExchange.Redis takes no
  per-call token

### Requirement: Redis stream keys are prefix-namespaced

The system SHALL map a logical destination to the Redis key `"{StreamPrefix}:{destination}"` with
`StreamPrefix` defaulting to `"birko:mq:stream"`.

#### Scenario: Default prefix

- **Given** default `RedisStreamSettings`
- **When** `GetStreamKey("events.order-placed")` is called
- **Then** the result is `"birko:mq:stream:events.order-placed"`

#### Scenario: Custom prefix isolates environments

- **Given** `StreamPrefix = "staging:mq"`
- **When** producer and consumer both use those settings
- **Then** they agree on `"staging:mq:events.order-placed"` and never see the default namespace

### Requirement: MQTT topic validation and wildcard matching

The system SHALL reject publish topics that are empty or contain `+`/`#`, reject subscribe filters
that are empty or place `#` anywhere but alone in the last level or `+` anywhere but alone in its
level, and match a topic against a filter level by level with MQTT wildcard semantics including the
`$`-prefixed-topic exclusion for leading wildcards.

#### Scenario: Publishing to a wildcard topic

- **Given** the topic `"sensors/+/temp"`
- **When** `MqttProducer.SendAsync` or `PublishAsync` is called
- **Then** `ArgumentException` is thrown naming the topic parameter

#### Scenario: Subscribing with a malformed filter

- **Given** the filter `"sensors/#/temp"` or `"sensors/a+/temp"`
- **When** `MqttConsumer.SubscribeAsync` is called
- **Then** `ArgumentException` is thrown

#### Scenario: Multi-level wildcard matches the parent level

- **Given** the filter `"sport/#"`
- **When** matched against the topic `"sport"`
- **Then** it matches (the `#` check precedes the exhausted-topic check)

#### Scenario: Single-level wildcard requires a level to exist

- **Given** the filter `"sport/+"`
- **When** matched against `"sport"`
- **Then** it does not match

#### Scenario: Leading wildcard and a system topic

- **Given** the filters `"#"`, `"+/x"` and `"$SYS/#"`
- **When** matched against `"$SYS/broker/uptime"`
- **Then** the first two do not match and `"$SYS/#"` does

#### Scenario: Level counts must agree without a multi-level wildcard

- **Given** the filter `"a/b"`
- **When** matched against `"a/b/c"`
- **Then** it does not match

### Requirement: MQTT dispatch replays subscriptions on reconnect and attaches its receiver once

The system SHALL attach the MQTTnet message-received handler exactly once (double-checked under a
lock), re-issue a broker SUBSCRIBE for every registered filter on each client `Connected` event
(best-effort, swallowing failures), and dispatch each received message to every registered filter that
matches, isolating handler exceptions behind the optional `OnHandlerError` hook.

#### Scenario: Concurrent first subscriptions

- **Given** two threads calling `SubscribeAsync` simultaneously on a fresh consumer
- **When** both reach `EnsureEventAttached`
- **Then** the received-message handler is attached exactly once, so each broker message is dispatched
  once

#### Scenario: Auto-reconnect with a clean session

- **Given** `CleanSession = true` (the default) and two active filters, and the broker drops the
  connection
- **When** the client reconnects
- **Then** both filters are re-subscribed at the broker so handlers keep receiving

#### Scenario: Resubscribe fails

- **Given** the broker rejects a resubscribe during reconnect
- **When** `ResubscribeAllAsync` runs
- **Then** the error is swallowed and the next reconnect retries

#### Scenario: Handler throws with no error hook

- **Given** two matching filters, the first of whose handlers throws, and `OnHandlerError` unset
- **When** a message arrives
- **Then** the exception is discarded, the second handler still runs, and the failure is unobservable

#### Scenario: Error hook itself throws

- **Given** `OnHandlerError` set to a callback that throws
- **When** a handler fails
- **Then** the hook's exception is swallowed and dispatch of remaining handlers continues

#### Scenario: Second subscription to the same filter

- **Given** an existing subscription for `"sensors/#"`
- **When** `SubscribeAsync("sensors/#", otherHandler)` is called
- **Then** the handler dictionary entry is **overwritten** — the earlier handler stops receiving,
  unlike the in-memory transport where both subscribers receive

#### Scenario: Handlers receive no cancellation token

- **Given** any MQTT subscription
- **When** a message is dispatched
- **Then** the handler is invoked with `CancellationToken.None`

### Requirement: MQTT message metadata travels as MQTT5 user properties

The system SHALL publish `QueueMessage.PayloadType` and the serialized `MessageHeaders` as MQTT5 user
properties (`payload_type`, `headers`) when present, and on receipt rebuild `PayloadType` and
`Headers` from those properties, stamping `CreatedAt` with the consumer's clock.

#### Scenario: Typed round-trip over MQTT5

- **Given** `SendAsync<T>(topic, payload)` and a subscriber for the same filter
- **When** the message is received
- **Then** `PayloadType` and `Headers` (including `ContentType`) are restored from user properties

#### Scenario: MQTT 3.1.1 broker or a foreign publisher

- **Given** a connection or publisher that does not carry user properties
- **When** a message is received
- **Then** `PayloadType` stays null and `Headers` keeps its defaults; the body still deserializes

#### Scenario: Message identity is not carried

- **Given** a producer-side `QueueMessage.Id`
- **When** the message is received
- **Then** the consumer constructs a new `QueueMessage` with a freshly generated `Id`, and `CreatedAt`
  is the receive time rather than the send time

#### Scenario: Empty payload

- **Given** a published message with an empty payload segment
- **When** it is received and a typed handler is registered
- **Then** the body is the empty string, deserialization is skipped, and the typed handler is not
  invoked

### Requirement: MQTT reconnection is bounded and its token source is lifecycle-managed

The system SHALL, on an unexpected disconnect and when `AutoReconnect` is true and the queue is not
disposed, raise `Disconnected`, cancel-and-dispose any previous reconnect `CancellationTokenSource`
and start a fresh one under a lock, then retry `ConnectAsync` every `ReconnectDelay` until connected,
cancelled, or `MaxReconnectAttempts` failures (0 meaning unlimited).

#### Scenario: Broker restarts

- **Given** `AutoReconnect = true`, `ReconnectDelay = 5s`, `MaxReconnectAttempts = 0`
- **When** the connection drops
- **Then** the client retries every 5 seconds indefinitely until connected

#### Scenario: Attempt cap reached

- **Given** `MaxReconnectAttempts = 3` and a permanently unreachable broker
- **When** three connect attempts have failed
- **Then** the reconnect loop exits and no further attempts are made

#### Scenario: Explicit disconnect during reconnect

- **Given** a reconnect loop in flight
- **When** `DisconnectAsync` or `Dispose` is called
- **Then** the reconnect CTS is cancelled and disposed under the lock, the loop breaks on
  `OperationCanceledException`, and no handle is leaked

#### Scenario: Disconnect after disposal

- **Given** a disposed queue
- **When** the broker raises a disconnect
- **Then** the `Disconnected` event still fires but no reconnect is started, and the MQTTnet event
  handlers were already detached by `Dispose`

### Requirement: MQTT subscription disposal unsubscribes at the broker only

The system SHALL, on `MqttSubscription.UnsubscribeAsync`, await a broker UNSUBSCRIBE for the
destination and flip `IsActive` false; `Dispose` SHALL flip `IsActive` first and fire the UNSUBSCRIBE
on a detached task swallowing failures. Neither SHALL remove the consumer's local handler
registration.

#### Scenario: Unsubscribe awaited

- **Given** an active MQTT subscription
- **When** `UnsubscribeAsync` is awaited
- **Then** the broker UNSUBSCRIBE completes and `IsActive` is false

#### Scenario: Disposed subscription and a later reconnect

- **Given** a subscription disposed while other subscriptions remain, then a reconnect
- **When** `ResubscribeAllAsync` replays the consumer's registered filters
- **Then** the disposed subscription's filter is still in `_handlers` and is re-subscribed, so its
  handler starts receiving again

#### Scenario: Unsubscribe twice

- **Given** an already-unsubscribed subscription
- **When** `UnsubscribeAsync` or `Dispose` is called again
- **Then** it returns immediately without issuing another broker call

### Requirement: Message payload serialization is pluggable with a JSON default

The system SHALL default every transport, the distributed bus and the outbox to
`JsonMessageSerializer` (System.Text.Json via `SystemJsonSerializer`, camelCase, non-indented) unless
an `IMessageSerializer` is supplied, and SHALL allow wrapping any serializer with
`EncryptingMessageSerializer`.

#### Scenario: Default serializer

- **Given** no serializer argument
- **When** an `InMemoryMessageQueue`, `RedisStreamQueue`, `MqttMessageQueue`, `DistributedEventBus`,
  `OutboxEventBus` or `OutboxProcessor` is constructed
- **Then** a `JsonMessageSerializer` with camelCase naming is used

#### Scenario: Encrypted payloads

- **Given** an `EncryptingMessageSerializer` over `JsonMessageSerializer` with matching encrypt and
  decrypt functions
- **When** a payload is serialized then deserialized
- **Then** the wire form is the ciphertext, the round-trip yields the original object, and
  `ContentType` is the inner content type suffixed with `"+encrypted"`

#### Scenario: Producer and consumer disagree on serializer

- **Given** a producer using `EncryptingMessageSerializer` and a consumer using plain JSON
- **When** a message is consumed
- **Then** the consumer's deserialization fails or yields null — the content type is informational
  only and no negotiation occurs

#### Scenario: Null constructor arguments

- **Given** a null inner serializer, encrypt or decrypt function
- **When** `EncryptingMessageSerializer` is constructed
- **Then** `ArgumentNullException` is thrown naming the offending parameter

### Requirement: Messaging pattern refinements are declared but unimplemented by the in-area backends

The system SHALL declare pattern-specific refinements of the transport contracts — `IPublisher` and
`ISender` extending `IMessageProducer`, `ISubscriber` and `IReceiver` extending `IMessageConsumer`, and
`ITransactionalProducer` adding `BeginTransactionAsync`/`CommitAsync`/`RollbackAsync` — and none of
the in-memory, MQTT or Redis Streams backends in this area SHALL implement any of them.

#### Scenario: Casting a backend producer to a pattern interface

- **Given** an `InMemoryMessageQueue`, `MqttMessageQueue` or `RedisStreamQueue`
- **When** its `Producer` is cast to `IPublisher`, `ISender` or `ITransactionalProducer`
- **Then** the cast fails (`as` yields null) — only the base `IMessageProducer` surface is available

#### Scenario: Pull-style receive on a backend consumer

- **Given** any of the three backends
- **When** its `Consumer` is cast to `IReceiver` to call `ReceiveAsync`
- **Then** the cast fails; pull-based reading exists only on the internal `InMemoryChannel.ReadAsync`
  and is not reachable through the public consumer contract

#### Scenario: Transactional send requested

- **Given** a need for messages invisible until commit
- **When** a backend's producer is used
- **Then** no transaction boundary is available; `SendAsync` publishes immediately

### Requirement: Message handler context is fully populated and guarded

The system SHALL construct a `MessageContext` only with a non-null message, destination and consumer,
throwing `ArgumentNullException` otherwise, and SHALL default `DeliveryCount` to 1 and `ReceivedAt` to
`DateTimeOffset.UtcNow`.

#### Scenario: Typed handler invocation

- **Given** a typed `IMessageHandler<T>` subscription on any of the three backends
- **When** a message arrives and its body deserializes to a non-null `T`
- **Then** the handler receives the payload plus a `MessageContext` naming the destination and the
  consumer, with `DeliveryCount == 1`

#### Scenario: Body does not deserialize

- **Given** a typed subscription and a message whose body is empty or deserializes to null
- **When** the message is dispatched on the in-memory, MQTT or Redis backend
- **Then** the typed handler is not invoked at all and no context is created

#### Scenario: Null construction argument

- **Given** a null message, destination or consumer
- **When** `MessageContext` is constructed
- **Then** `ArgumentNullException` is thrown naming that parameter

### Requirement: In-memory queue options supply channel capacity

The system SHALL accept an `InMemoryMessageQueueOptions` whose `ChannelCapacity` (default 1000) is
forwarded to the per-destination bounded channels, throwing `ArgumentNullException` when the options
object is null.

#### Scenario: Custom capacity

- **Given** `new InMemoryMessageQueueOptions { ChannelCapacity = 10 }`
- **When** an `InMemoryMessageQueue` is constructed from it and 10 messages are buffered unconsumed
- **Then** the eleventh `SendAsync` waits for space

#### Scenario: Null options

- **Given** a null options argument
- **When** the options-taking constructor is invoked
- **Then** `ArgumentNullException` is thrown naming `options`

### Requirement: Content-based message fingerprinting

The system SHALL compute a lower-case hex SHA-256 fingerprint of a message body, of a
`QueueMessage`'s body, or of `destination + "\0" + body`, throwing `ArgumentNullException` for any
null argument.

#### Scenario: Identical bodies

- **Given** two messages with byte-identical bodies
- **When** `MessageFingerprint.Compute` is called on each
- **Then** the fingerprints are equal

#### Scenario: Same body, different destinations

- **Given** one body sent to `"a"` and to `"b"`
- **When** the composite overload is used
- **Then** the fingerprints differ

#### Scenario: Null input

- **Given** a null body or null `QueueMessage`
- **When** `Compute` is called
- **Then** `ArgumentNullException` names `body` or `message` rather than throwing
  `NullReferenceException`

### Requirement: Retry delay computation saturates instead of overflowing

The system SHALL return `BaseDelay` unchanged when `UseExponentialBackoff` is false, and otherwise
compute `BaseDelay.Ticks * 2^(attemptNumber-1)` in floating point, returning `MaxDelay` whenever that
value reaches or exceeds `MaxDelay.Ticks`.

#### Scenario: Fixed delay

- **Given** `UseExponentialBackoff = false`, `BaseDelay = 5s`
- **When** `GetDelay(7)` is called
- **Then** the result is 5 seconds

#### Scenario: Exponential growth then clamp

- **Given** defaults (`BaseDelay = 5s`, `MaxDelay = 5m`)
- **When** `GetDelay(1)`, `GetDelay(2)`, `GetDelay(3)` and `GetDelay(100)` are called
- **Then** the results are 5s, 10s, 20s and exactly 5 minutes — no negative or wrapped value escapes
  the clamp

#### Scenario: Attempt number below one

- **Given** defaults and `GetDelay(0)`
- **When** the delay is computed
- **Then** the result is 2.5 seconds (`2^-1 × BaseDelay`); the method does not clamp the attempt
  number to a minimum of 1

#### Scenario: Preset policies

- **Given** `RetryPolicy.Default` and `RetryPolicy.None`
- **When** each is read
- **Then** `Default` allows 3 retries with exponential backoff from 5s and `None` sets `MaxRetries` to
  0; each access returns a **new** instance, so mutating one does not affect another

### Requirement: Dead-letter destination naming

The system SHALL derive a dead-letter destination as the explicit `DeadLetterOptions.Destination` when
set, otherwise the source destination with `Suffix` (default `".dlq"`) appended.

#### Scenario: Suffix-based naming

- **Given** default `DeadLetterOptions`
- **When** `GetDeadLetterDestination("events.order-placed")` is called
- **Then** the result is `"events.order-placed.dlq"`

#### Scenario: Explicit destination overrides the suffix

- **Given** `Destination = "all-failures"`
- **When** the method is called for any source destination
- **Then** the result is `"all-failures"`

#### Scenario: Disabled option is not enforced here

- **Given** `Enabled = false`
- **When** `GetDeadLetterDestination` is called
- **Then** a name is still returned — none of the transports in this area consume `DeadLetterOptions`,
  so the flag has no effect on delivery

### Requirement: MQTT settings identity and copy semantics

The system SHALL compose `MqttSettings.GetId()` as the base remote-settings id plus `":"` and the
`ClientId` (or `"auto"` when null), and SHALL copy MQTT-specific fields in `LoadFrom(MqttSettings)`
while sharing `ClientCertificate` and `LastWill` by reference.

#### Scenario: Default settings identity

- **Given** default `MqttSettings` (localhost:1883) with no `ClientId`
- **When** `GetId()` is called
- **Then** the id ends with `":auto"`

#### Scenario: Loading from another MqttSettings

- **Given** a source instance with a client certificate
- **When** `LoadFrom(source)` is called
- **Then** every MQTT option is copied and the target shares the same `X509Certificate2` instance, so
  disposing the source's certificate breaks the target

#### Scenario: Loading from a non-MQTT Settings instance

- **Given** a plain `Settings` (or any non-`MqttSettings`) instance
- **When** `LoadFrom(Settings)` is called
- **Then** nothing is copied at all — not even the base location/name — and the call reports success

### Requirement: DI registration surface for the event bus

The system SHALL register `InProcessEventBus` as a singleton `IEventBus` built from the root provider
plus all registered `IEventPipelineBehavior` and `IEventEnricher` services, register handlers as
transient `IEventHandler<TEvent>`, and register behaviours/enrichers as singletons in call order.

#### Scenario: Standard wiring

- **Given** `AddEventBus()` then `AddEventHandler<OrderPlaced, OrderPlacedHandler>()`
- **When** `IEventBus` is resolved and an `OrderPlaced` is published
- **Then** the handler is resolved from the root provider and invoked

#### Scenario: Handler with a scoped dependency

- **Given** a handler registered transiently that depends on a scoped service
- **When** the singleton bus resolves handlers from the root provider during publish
- **Then** resolution of the scoped dependency fails — publication is not wrapped in a DI scope

#### Scenario: Deduplication convenience registration

- **Given** `AddEventDeduplication(TimeSpan.FromMinutes(30))`
- **When** the container is built
- **Then** a singleton `InMemoryDeduplicationStore` with a 30-minute TTL and a `DeduplicationBehavior`
  pipeline behaviour are registered

#### Scenario: Custom bus implementation

- **Given** `AddEventBus<MyBus>()`
- **When** `IEventBus` is resolved
- **Then** `MyBus` is returned as a singleton, and no `InProcessEventBusOptions` is registered

#### Scenario: Behaviour ordering

- **Given** `AddEventPipelineBehavior<A>()` then `AddEventPipelineBehavior<B>()`
- **When** the bus builds its pipeline from `GetServices<IEventPipelineBehavior>()`
- **Then** `A` is outermost and `B` inner, following registration order
