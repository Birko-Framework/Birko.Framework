---
area: event-bus-and-messaging
generated-at: c50ed580c4d08391a4ae55a7e8b62c394f6e3469
generated-on: 2026-10-12
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
  - ../Birko.EventBus/Live/KeyedEventBroadcaster.cs
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
# source-commits: omitted — since the monorepo migration (TASK-457) every source above is inside this
# repo, so `generated-at` covers them (regen step 5c). The old per-sibling shas no longer resolve here.
shaped-by: [FEATURE-014]
# Evidence pass ran 2026-10-12 over 325 feature-linked tasks: 59 resolved, 266 did not —
# polyrepo-era `pr:` shas the history import rewrote, or no reachable commit whose subject leads with the
# task id; 9 of those are refused by the state gate (todo/cancelled). Earlier entries kept append-only.
shaped-by-derived: true
shaped-by-unresolved: 266
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

The system SHALL give every `EventBase`-derived event a fresh `EventGuid` (`Guid.NewGuid()`) and an
`OccurredAt` read from the static, replaceable `EventBase.DefaultClock` at construction, and SHALL
require each concrete event to supply `Source` via an abstract override.

#### Scenario: Two events constructed in sequence

- **Given** a sealed record deriving from `EventBase`
- **When** two instances are constructed
- **Then** their `EventGuid` values differ, and each `OccurredAt` equals the value
  `EventBase.DefaultClock.UtcNow` returned at construction time

#### Scenario: Test clock substituted

- **Given** `EventBase.DefaultClock` is assigned a provider returning a fixed `UtcNow`
- **When** a new event is constructed without an explicit `OccurredAt`
- **Then** `OccurredAt` equals that fixed value (the assignment is process-global static state)

#### Scenario: Caller overrides the defaults

- **Given** an event type with `init` accessors inherited from `EventBase`
- **When** it is constructed with explicit `EventGuid` and `OccurredAt` values
- **Then** the supplied values are kept and no new Guid or clock read replaces them

### Requirement: EventContext construction from an event

The system SHALL build an `EventContext` from an `IEvent` via `EventContext.From`, copying `EventGuid`
and `Source`, copying `CorrelationGuid` from any event that implements `ICorrelatedEvent`
(`Guid? CorrelationGuid`, implemented by `EventBase`; otherwise null), defaulting `DeliveryCount` to 1
and `Metadata` to an empty dictionary when none is supplied. `EventContext.Source` SHALL default to
`string.Empty`, never null.

#### Scenario: Event derives from EventBase with a correlation id

- **Given** an `EventBase` event whose `CorrelationGuid` is set
- **When** `EventContext.From(@event)` is called
- **Then** `context.CorrelationGuid` equals the event's `CorrelationGuid`

#### Scenario: Event implements IEvent directly

- **Given** a type that implements `IEvent` and `ICorrelatedEvent` without deriving from `EventBase`
- **When** `EventContext.From(@event)` is called
- **Then** `context.CorrelationGuid` equals the event's `CorrelationGuid`; a type implementing only
  `IEvent` yields null (SH-L124; only an `EventBase` was read, so a direct `IEvent` implementation's
  correlation id was dropped)

#### Scenario: Context constructed without a source

- **Given** an `EventContext` created without setting `Source`
- **When** `Source` is read
- **Then** it is `string.Empty` (SH-L125; it was declared `null!` and read null)

#### Scenario: Tenant is never inferred

- **Given** any event
- **When** `EventContext.From(@event)` is called without the `tenantGuid` argument
- **Then** `context.TenantGuid` is null — tenant is only ever populated by an `IEventEnricher` or by
  an explicit caller argument

### Requirement: In-process publication dispatches to DI handlers plus manual subscriptions

The system SHALL, on `InProcessEventBus.PublishAsync<TEvent>`, throw `ObjectDisposedException` if the
bus is disposed, run every registered `IEventEnricher` in order, then collect handlers as
`IEnumerable<IEventHandler<TEvent>>` resolved from a fresh async DI scope opened for this publish (from
the supplied `IServiceProvider`'s `IServiceScopeFactory`; directly from the provider when it has none)
followed by manually `Subscribe`d handlers, and dispatch to them. The scope lives for the whole dispatch,
is shared by every handler of that publish, and is disposed when the publish ends (TASK-565, SH-M120).

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

### Requirement: In-process publication runs the pipeline even when there are no handlers

The system SHALL, in `InProcessEventBus.PublishAsync`, run enrichment and then the
`IEventPipelineBehavior` chain for every publish, including one whose collected handler list is empty,
so behaviours such as deduplication and auditing observe every published event.

#### Scenario: Deduplication behaviour registered but no handler for the event type

- **Given** `AddEventDeduplication()` is registered and no handler exists for `OrderPlaced`
- **When** `OrderPlaced` with a given `EventGuid` is published
- **Then** `DeduplicationBehavior` runs and records the `EventGuid`, so a later publish of the same
  `EventGuid` (once a handler exists) is dropped as a duplicate (SH-M119; it returned before the
  pipeline, so the id was not recorded and the later publish was treated as first-seen)

#### Scenario: Enrichers still run

- **Given** a `CorrelationEventEnricher` is registered and no handler exists for the event type
- **When** the event is published
- **Then** the enricher runs to completion, then the pipeline runs around an empty dispatch

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
the first captured failure via `ExceptionDispatchInfo` after `Task.WhenAll`. An
`OperationCanceledException` SHALL be swallowed only when that Stop-mode token source was cancelled
(and the caller's token was not); any other handler failure, including a handler's own
`OperationCanceledException`, SHALL be handled as a failure.

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

#### Scenario: A handler throws its own OperationCanceledException in Continue mode

- **Given** `MaxConcurrency > 1`, `ErrorHandling = Continue`, and a handler that throws
  `OperationCanceledException` from an internal timeout while neither the bus's token source nor the
  caller's token is cancelled
- **When** the event is published
- **Then** the failure is reported through `OnHandlerError` and the other handlers run (SH-L117; any
  `OperationCanceledException` was swallowed unreported)

### Requirement: Event subscriptions are disposable and idempotent

The system SHALL return an `IEventSubscription` from `Subscribe` that reports its `EventType` and
`IsActive`, and whose `Dispose` removes the handler exactly once, even under concurrent calls. The
transport subscriptions (`InMemorySubscription`, `RedisSubscription`, `MqttSubscription`) SHALL make
`Dispose`/`UnsubscribeAsync` idempotent the same way, via `Interlocked`. `InProcessEventBus` SHALL
serialize `Subscribe` and `Dispose`, throw `ObjectDisposedException` from `Subscribe` after disposal,
and report `IsActive == false` on every issued subscription once the bus is disposed.

#### Scenario: In-process subscription disposed twice concurrently

- **Given** an `InProcessEventBus` subscription
- **When** `Dispose()` is called twice, possibly from two threads
- **Then** the unsubscribe action runs exactly once (guarded by `Interlocked.Exchange` on an int flag)
  and `IsActive` reports false afterwards

#### Scenario: Distributed subscription disposed concurrently

- **Given** a `DistributedEventBus` subscription
- **When** `Dispose()` is called from two threads simultaneously
- **Then** the unsubscribe action runs exactly once (an `Interlocked` guard, as on the in-process
  subscription) and `IsActive` reports false (SH-L115; the guard was a plain non-atomic `bool`
  check-then-set, so both callers could pass it and the unsubscribe action could run twice)

#### Scenario: Transport subscription disposed concurrently

- **Given** an `InMemorySubscription`, `RedisSubscription` or `MqttSubscription`
- **When** `Dispose()` or `UnsubscribeAsync()` is called from two threads simultaneously
- **Then** the teardown runs exactly once (SH-L115; a non-atomic check let both callers tear down)

#### Scenario: Bus disposed while subscriptions are live

- **Given** an `InProcessEventBus` with active subscriptions
- **When** `Dispose()` is called
- **Then** the subscription dictionary is cleared and the previously returned `IEventSubscription`
  handles report `IsActive == false` (SH-L118; they still reported `IsActive == true`)

#### Scenario: Subscribe races or follows disposal

- **Given** an `InProcessEventBus`
- **When** `Subscribe` is called concurrently with, or after, `Dispose()`
- **Then** the two are serialized: a `Subscribe` after disposal throws `ObjectDisposedException`, and no
  handler is added to a cleared bus (SH-L118; unsynchronised, a `Subscribe` could add a handler after
  the clear and report an active subscription on a disposed bus)

### Requirement: A keyed live broadcaster never blocks the publisher and keeps each subscriber in order

The system SHALL provide `KeyedEventBroadcaster<TKey, TEvent>` (TASK-533) whose `Publish(key, event)` is synchronous,
never blocks and never throws, stamps the key's next sequence (from 1) and writes to every current subscriber of the
key under the key's lock, so concurrent publishers cannot reorder a subscriber's sequence. Each subscriber SHALL have a
bounded buffer (`capacity`, at least 1) that drops its oldest event when full, counting it in `Dropped`. An event for a
key with no subscriber SHALL be discarded and `Publish` SHALL return false. `Subscribe` MAY precede the first event;
`Complete(key, error)` SHALL complete every reader of the key (throwing `error` after the buffered events) and forget
the key; a key whose last subscriber leaves SHALL be forgotten, so its sequence restarts.

#### Scenario: A slow subscriber

- **Given** two subscribers with capacity 4, one that never reads
- **When** 1000 events are published
- **Then** the publisher is not slowed, the reading subscriber gets all 1000 in order, and the other reads sequences
  997–1000 with `Dropped = 996`

#### Scenario: Concurrent publishers

- **Given** 8 threads publishing 2000 events each to one key
- **When** a subscriber reads them
- **Then** its sequences are exactly 1..16000 in order (with the write outside the lock, as DraCode had it, this fails)

### Requirement: Deduplication reserves before handling and releases on failure (at-least-once)

The system SHALL, in `DeduplicationBehavior`, atomically reserve the event's `EventGuid` via
`IDeduplicationStore.TryMarkProcessedAsync` **before** invoking the rest of the pipeline, and skip the
event entirely when the reservation fails. When the rest of the pipeline throws, the behaviour SHALL
release the reservation through the required `IDeduplicationStore.ReleaseAsync(Guid, CancellationToken)`
(no default implementation; `InMemoryDeduplicationStore` removes the entry) and rethrow, so a retry or
redelivery runs the handlers again.

#### Scenario: Same EventGuid published twice

- **Given** a `DeduplicationBehavior` over an `InMemoryDeduplicationStore`
- **When** the same event instance (same `EventGuid`) is published twice
- **Then** handlers run on the first publish only; the second returns without calling `next()`

#### Scenario: Two concurrent publishes of one EventGuid

- **Given** the `InMemoryDeduplicationStore` (whose `TryMarkProcessedAsync` is a
  `ConcurrentDictionary.TryAdd`)
- **When** two threads publish the same `EventGuid` simultaneously
- **Then** exactly one wins the `TryAdd` and runs handlers; the other is dropped

#### Scenario: Handler throws after the mark

- **Given** the event was marked and a downstream handler throws
- **When** the same event is republished later
- **Then** the reservation was released when the handler threw, so the republish runs the handlers
  again (SH-M118, decided by the user; it kept the mark, so a retry or redelivery was dropped as a
  duplicate and the failed processing was never retried)

#### Scenario: A custom store implements only Exists + Mark

- **Given** an `IDeduplicationStore` that does not override the interface's default
  `TryMarkProcessedAsync`
- **When** two callers publish the same `EventGuid` concurrently
- **Then** the default composed `ExistsAsync`-then-`MarkProcessedAsync` implementation is not atomic and
  both callers may observe "not seen" and both run handlers

### Requirement: In-memory deduplication expiry is swept lazily under a claimed interval

The system SHALL retain processed `EventGuid`s in `InMemoryDeduplicationStore` for a TTL (default 1
hour) and SHALL sweep entries older than `now - ttl` at most once per 5-minute interval, from
`ExistsAsync`, `TryMarkProcessedAsync` and `MarkProcessedAsync`, with the sweep slot claimed atomically
via `Interlocked.CompareExchange`.

#### Scenario: Entry older than the TTL

- **Given** an `EventGuid` marked more than the TTL ago and at least 5 minutes since the last sweep
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

#### Scenario: MarkProcessedAsync also sweeps

- **Given** only `MarkProcessedAsync` is ever called (never `Exists`/`TryMark`)
- **When** entries age past the TTL and the sweep interval has elapsed
- **Then** the next `MarkProcessedAsync` runs the sweep and the expired entries are removed (SH-L127;
  the sweep was triggered only from `ExistsAsync` and `TryMarkProcessedAsync`, so the store grew
  without bound)

### Requirement: Correlation enrichment fills the context but never the event

The system SHALL, in `CorrelationEventEnricher`, leave a non-null `context.CorrelationGuid` untouched,
otherwise copy the `CorrelationGuid` of an event implementing `ICorrelatedEvent` (as `EventBase` does) when
it has a value, and otherwise generate a fresh `Guid`.

#### Scenario: Event carries a correlation id

- **Given** an `ICorrelatedEvent` (an `EventBase`, or any event implementing the interface) with `CorrelationGuid` set and a context whose `CorrelationGuid` is null
- **When** the enricher runs
- **Then** the context adopts the event's value (SH-L124; only an `EventBase` was read, so a plain event's correlation was replaced by a fresh `Guid` and the trace broke)

#### Scenario: Neither event nor context carries one

- **Given** an event with a null `CorrelationGuid`
- **When** the enricher runs
- **Then** the context receives a newly generated `Guid` and the event itself is unmodified

#### Scenario: Context already enriched by an earlier enricher

- **Given** a preceding enricher set `context.CorrelationGuid`
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
- **Then** the context contains `EventGuid`, `OccurredAt`, `Source`, `DeliveryCount`, plus `TenantGuid`
  and `CorrelationGuid` when present, then every readable public instance property of the event, then
  the context metadata — later keys added with `TryAdd`, so the fixed keys win on collision

#### Scenario: Event exposes an indexer or a throwing property getter

- **Given** an event type declaring `this[int]` and a property whose getter throws
- **When** the default rule context is built
- **Then** the indexer is skipped and the throwing property is omitted; construction succeeds

#### Scenario: Constructed with a null rule set

- **Given** a null `RuleSet`
- **When** either `RuleFilterBehavior` constructor is invoked
- **Then** `ArgumentNullException` is thrown naming `ruleSet` (SH-L126; one constructor accepted null and
  the behaviour failed later with `NullReferenceException` at the first publish)

### Requirement: Topic naming for transport routing

The system SHALL derive a topic from an event type by kebab-casing the type name under the `events.`
prefix (`DefaultTopicConvention.GetTopic(Type)`), and from an event *instance* by prefixing with the
lower-cased `Source` when it is non-empty (`GetTopic(IEvent)`). Routing SHALL use `GetTopic(Type)`
only; the instance overload `GetTopic(IEvent)` does not decide routing, as `ITopicConvention`
documents.

#### Scenario: Type-based topic

- **Given** an event type named `OrderPlaced`
- **When** `DefaultTopicConvention.GetTopic(typeof(OrderPlaced))` is called
- **Then** the result is `"events.order-placed"`

#### Scenario: Instance-based topic with a source

- **Given** an `OrderPlaced` instance whose `Source` is `"Sales"`
- **When** `DefaultTopicConvention.GetTopic(@event)` is called
- **Then** the result is `"sales.order-placed"` — a different topic from the type-based form, which no
  bus uses for routing (SH-L128; the interface did not say which overload routes, so a custom
  convention could implement only the instance overload and see it ignored)

#### Scenario: Attribute overrides the convention

- **Given** an event class annotated `[Topic("custom.orders")]`
- **When** `AttributeTopicConvention.GetTopic` is called with either the type or an instance
- **Then** the result is `"custom.orders"`, cached per type in a `ConcurrentDictionary`

#### Scenario: Attribute-less event under the attribute convention

- **Given** an event with no `TopicAttribute` and `Source == "Sales"`
- **When** `AttributeTopicConvention.GetTopic(@event)` is called
- **Then** it delegates to `DefaultTopicConvention.GetTopic(@event)` and returns the source-prefixed
  `"sales.order-placed"`, agreeing with the default convention rather than the interface's
  type-delegating default; whether the type carries `[Topic]` is looked up once per type and cached
  (SH-L129; the attribute was reflected on every instance call)

#### Scenario: Topic attribute constructed with null

- **Given** `new TopicAttribute(null)`
- **When** the attribute is constructed
- **Then** `ArgumentNullException` is thrown

### Requirement: Distributed publication serialises an envelope onto a type-derived topic

The system SHALL, on `DistributedEventBus.PublishAsync`, run enrichers, build an `EventEnvelope`
carrying `EventGuid`, the event's `AssemblyQualifiedName`, `Source`, `OccurredAt`, the context's
`CorrelationGuid`/`TenantGuid`/`Metadata` and the serialized event as `Payload`, and send the serialized
envelope as a `QueueMessage` to `TopicConvention.GetTopic(@event.GetType())`.

#### Scenario: Event published to the transport

- **Given** a `DistributedEventBus` over an `IMessageQueue`
- **When** `PublishAsync(new OrderPlaced(...))` is called
- **Then** one `QueueMessage` is sent to `"events.order-placed"` whose `PayloadType` is
  `typeof(EventEnvelope).AssemblyQualifiedName`, whose `Headers.CorrelationId` is the context
  correlation id as a string, and whose `Headers.ContentType` is the serializer's content type

#### Scenario: Envelope constructed without values

- **Given** an `EventEnvelope` created by its parameterless constructor (as a deserializer does)
- **When** `EventType`, `Source` or `Payload` is read before being set
- **Then** each is `string.Empty`, never null (SH-L125; they were declared `null!` and read null)

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

### Requirement: Distributed delivery reconstructs the event and its context, reports dropped messages and redelivers unknown types

The system SHALL, in the transport delivery callback, deserialize the `EventEnvelope`, resolve
`envelope.EventType` via `Type.GetType`, require it to be assignable to `TEvent`, deserialize the
payload, and rebuild an `EventContext` from the envelope. When the envelope or the payload
deserializes to null, or the resolved type is not assignable to `TEvent`, it SHALL raise
`DistributedEventBus.MessageDropped` (`Action<QueueMessage, string reason>`, a throwing subscriber
ignored) and return, acknowledging the message. When the event type cannot be loaded in this process
it SHALL throw `InvalidOperationException`, so the transport redelivers the message.

#### Scenario: Well-formed envelope

- **Given** an envelope whose `EventType` resolves to `OrderPlaced` and whose payload deserializes
- **When** the message is delivered
- **Then** handlers receive the event and an `EventContext` carrying the envelope's `EventGuid`,
  `Source`, `CorrelationGuid`, `TenantGuid` and `Headers`

#### Scenario: Event type not present in the consuming process

- **Given** an envelope whose `EventType` assembly-qualified name cannot be resolved locally
- **When** the message is delivered
- **Then** the callback throws `InvalidOperationException` naming the type, so the transport leaves the
  message for redelivery and a consumer that knows the type can take it (SH-M116, decided by the user;
  it returned without throwing, so the message was acknowledged and lost)

#### Scenario: Envelope body is not an EventEnvelope

- **Given** a message whose body deserializes to null as `EventEnvelope`
- **When** the message is delivered
- **Then** `MessageDropped` is raised with the message and a reason, and the callback returns
  (SH-M116; it returned silently, so the drop was unobservable)

#### Scenario: Event type is not the subscribed type, or the payload does not decode

- **Given** an envelope whose type resolves but is not assignable to `TEvent`, or whose payload
  deserializes to null
- **When** the message is delivered
- **Then** `MessageDropped` is raised with a reason and the message is acknowledged; an exception
  thrown by a `MessageDropped` subscriber is ignored (SH-M116; it returned silently)

#### Scenario: Delivery count header present

- **Given** a message redelivered by a Redis reclaim (`XAUTOCLAIM`) or requeued by an in-memory
  `RejectAsync(requeue: true)`
- **When** the message is delivered
- **Then** `context.DeliveryCount` is the transport's count — the callback reads the custom header
  `x-delivery-count` (its parsed value when present, 1 when missing or unparseable), which a Redis
  reclaim sets from the entry's PEL delivery count (`XPENDING`) and an in-memory requeue increments
  (a missing header counting as 1); a first delivery, and every MQTT delivery, reports 1 (SH-L113; no
  producer or consumer wrote that key, so a handler could not tell a redelivery from a first attempt)

#### Scenario: No handler registered for the event type

- **Given** a transport subscription exists but no DI or manual handler for `TEvent`
- **When** a message arrives
- **Then** the pipeline still runs around an empty dispatch, so behaviours such as deduplication record
  the `EventGuid` (SH-M119; the callback returned before running the pipeline)

### Requirement: Distributed delivery resolves DI handlers from one scope per message

The system SHALL resolve `IEnumerable<IEventHandler<TEvent>>` for each delivered message from a fresh
async DI scope opened for that message (from the bus's `IServiceProvider`'s `IServiceScopeFactory`;
directly from the provider when it has none), followed by the manually `Subscribe`d handlers, and SHALL
dispose the scope once dispatch, pipeline included, is over (TASK-566, the contract `InProcessEventBus`
has since TASK-565).

#### Scenario: Handler with a scoped dependency

- **Given** a handler registered transiently that depends on a scoped service, in a root built with
  `ValidateScopes = true`
- **When** two messages are delivered
- **Then** each runs with its own scoped instance, disposed after its dispatch (before TASK-566 the
  resolve from the root threw under `ValidateScopes` and otherwise shared one root-owned instance)

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
- **Then** the fault is reported through `InMemoryConsumer.OnHandlerError` and the dispatch loop
  continues; the in-memory transport does not redeliver by itself — under `ManualAck` the entry stays
  pending so `RejectAsync(requeue: true)` redelivers it (SH-M109/SH-M110; the dispatch loop discarded the
  fault unobserved, so the rethrow had no observable effect on this backend)

#### Scenario: Retry and dead-letter on a transport that can count deliveries

- **Given** `DistributedEventBusOptions.RetryPolicy` (the shared `Birko.RetryPolicy`) and/or `DeadLetterOptions`
  set (both default to null; `DeadLetterOptions` alone bounds retries with `Birko.RetryPolicy.Default`), over a
  transport whose `IRedeliveryCapability` reports `Resend` (in-memory) or `Redelivered` (Redis with a consumer
  group and `PendingRetryMilliseconds` above 0)
- **When** a delivery's dispatch keeps failing
- **Then** it is retried while its `x-delivery-count` is at most `MaxRetries`: on in-memory as a re-send of a
  copy with the count incremented and `Delay = RetryPolicy.GetDelay(count)`; on Redis by letting the delivery
  fault, so the transport reclaims the pending entry (its delay is `PendingRetryMilliseconds`, not the policy's)
- **And** once the count passes `MaxRetries` a copy is sent to `DeadLetterOptions.GetDeadLetterDestination(topic)`
  (`{topic}.dlq` by default) with `x-original-destination` and `x-dead-letter-reason`, and the delivery completes
  (acknowledged); with no enabled dead-letter options it is dropped and reported through `MessageDropped`
  (TASK-668, implementing what SH-L114 refused; measured: three deliveries then the DLQ in memory, two then the
  DLQ live on Redis)

#### Scenario: Retry options on a transport that cannot count deliveries

- **Given** `RetryPolicy` or `DeadLetterOptions` set
- **When** the `DistributedEventBus` is constructed over a transport without `IRedeliveryCapability` (MQTT), or
  one reporting `None` (Redis without a consumer group, or with reclaim off)
- **Then** `NotSupportedException` is thrown, since the rule would never fire (SH-L114's refusal, kept where it
  still applies; a negative `MaxRetries` is `ArgumentOutOfRangeException`)

### Requirement: Distributed bus disposal unsubscribes synchronously or asynchronously

The system SHALL dispose each transport `ISubscription` synchronously in `Dispose()` and await
`UnsubscribeAsync` on each in `DisposeAsync()`, clearing the manual-handler dictionary in both cases,
and SHALL make both paths no-ops after the first, `Dispose` and `DisposeAsync` racing to a single
teardown.

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

#### Scenario: Dispose and DisposeAsync called concurrently

- **Given** a bus with live transport subscriptions
- **When** `Dispose()` and `DisposeAsync()` are called from two threads at once
- **Then** exactly one of them tears the subscriptions down and the other returns (SH-L116; a
  non-atomic flag let both pass and dispose each subscription twice)

### Requirement: Automatic transport subscription from DI-registered handlers

The system SHALL, in `AutoSubscriber.SubscribeAllAsync`, scan every assembly in the current
`AppDomain` for non-abstract, non-interface types implementing `IEventHandler<T>`, keep those `T` that
are classes assignable to `IEvent` **and** for which DI has an `IEventHandler<T>` registration, and
call `DistributedEventBus.SubscribeToTransportAsync<T>` reflectively for each distinct `T`. The
registration SHALL be asked of `IServiceProviderIsService` without building a handler, and only for a
container that does not offer it, of a short-lived scope; never by resolving handlers from the root,
which under `ValidateScopes` threw at startup for any handler with a scoped dependency (TASK-566).

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

### Requirement: Hosted auto-subscription takes the DistributedEventBus by its concrete type

The system SHALL, in `AddDistributedEventBus`, register `DistributedEventBus` as its own singleton and
map `IEventBus` to that instance; `DistributedEventBusHostedService` SHALL take the `DistributedEventBus`
itself in its constructor (null throwing `ArgumentNullException`), never cast the resolved `IEventBus`.
`AddDistributedEventBus` SHALL always register `DistributedEventBusHostedService`, which SHALL read
`DistributedEventBusOptions.AutoSubscribe` in `StartAsync` and run the auto subscriber only when it is
true.

#### Scenario: Distributed bus registered alone

- **Given** `AddDistributedEventBus()` with `AutoSubscribe` left at its default true
- **When** the host starts
- **Then** the hosted service is registered and `AutoSubscriber.SubscribeAllAsync` runs

#### Scenario: AutoSubscribe disabled

- **Given** `AddDistributedEventBus(o => o.AutoSubscribe = false)`
- **When** the host starts
- **Then** the hosted service is registered and its `StartAsync` returns immediately without
  subscribing

#### Scenario: Options changed after registration, before start

- **Given** `AddDistributedEventBus(o => o.AutoSubscribe = false)` and the registered
  `DistributedEventBusOptions` instance later set to `AutoSubscribe = true` before the host starts
- **When** the host starts
- **Then** `AutoSubscriber.SubscribeAllAsync` runs (SH-L112; the hosted service was registered only when
  `AutoSubscribe` was true at registration, so a later change was ignored)

#### Scenario: Outbox decorator applied over the distributed bus

- **Given** `AddDistributedEventBus()` followed by `AddOutboxEventBus()`, so `IEventBus` resolves to an
  `OutboxEventBus`
- **When** the hosted service is constructed at host startup
- **Then** it receives the registered `DistributedEventBus` singleton (the instance the decorator wraps)
  and the host starts and auto-subscribes (SH-M114; the hosted service cast `IEventBus` to
  `DistributedEventBus`, the cast yielded null and `InvalidOperationException` broke host startup)

### Requirement: Outbox publication persists instead of dispatching

The system SHALL, in `OutboxEventBus.PublishAsync`, run enrichers and then save an `OutboxEntry`
(new `Guid`, the event's `EventGuid`, assembly-qualified `EventType`, serialized `Payload`, `Source`,
context `CorrelationGuid`/`TenantGuid`, copied `Headers`, `Status = Pending`, `CreatedAt` from the
optional `IDateTimeProvider` passed to the constructor, else the system clock) to the `IOutboxStore`
without invoking the inner bus, and SHALL delegate `Subscribe` to the inner bus, throwing
`ObjectDisposedException` from both after `Dispose`. `OutboxEntry.EventType`, `Payload` and `Source`
SHALL default to `string.Empty`, never null.

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

#### Scenario: Subscribing after disposal

- **Given** a disposed `OutboxEventBus`
- **When** `Subscribe(handler)` is called
- **Then** `ObjectDisposedException` naming the outbox bus is thrown (SH-L120; the call was forwarded to
  the disposed inner bus)

#### Scenario: Entry stamped by a substituted clock

- **Given** an `OutboxEventBus` constructed with an `IDateTimeProvider` returning a fixed time
- **When** an event is published
- **Then** the saved entry's `CreatedAt` is that fixed time (SH-L123; it read `DateTime.UtcNow`, so it
  could disagree with the processor's retention clock)

#### Scenario: Entry constructed without values

- **Given** an `OutboxEntry` created by its parameterless constructor
- **When** `EventType`, `Payload` or `Source` is read before being set
- **Then** each is `string.Empty` (SH-L125; they were declared `null!` and read null)

### Requirement: Outbox batch processing publishes, marks, and never aborts the batch on one bad entry

The system SHALL, in `OutboxProcessor.ProcessBatchAsync`, fetch up to `BatchSize` pending entries,
and for each: mark it failed when its `EventType` cannot be resolved or its payload cannot be
deserialized to `IEvent`, otherwise publish it through the inner bus (reflectively, as the concrete
event type, through a closed `PublishAsync` `MethodInfo` built once per event type and cached —
SH-L119; it was rebuilt for every entry) inside the scope reconstructed by `IEventScopeAccessor` and
mark it published; any
exception SHALL be unwrapped from `TargetInvocationException` and recorded via `MarkFailedAsync` with
the configured `MaxAttempts`. Every entry — succeeded or failed — SHALL count toward the returned
processed count. An `OperationCanceledException` raised while the processing token is cancelled SHALL
end the batch without `MarkFailedAsync` — a shutdown is not a failed attempt.

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
- **Then** `OperationCanceledException` propagates, no attempt is recorded, and the remaining claimed
  entries stay in `Publishing` until a stale-claim reclaim returns them to `Pending`

#### Scenario: Shutdown while an entry is being published

- **Given** a claimed batch and the processing token cancelled while an entry's publish is in flight
- **When** the publish throws `OperationCanceledException`
- **Then** the batch ends and `ProcessBatchAsync` returns normally with the count so far; the entry's
  `Attempts` is not incremented and it and the rest of the batch stay `Publishing` until the stale-claim
  reclaim returns them (SH-M121; it was recorded through `MarkFailedAsync` as a failed attempt with the
  already-cancelled token, so that store call threw too)

#### Scenario: Empty outbox

- **Given** no pending entries
- **When** `ProcessBatchAsync` runs
- **Then** it returns 0 without publishing anything

### Requirement: Outbox scope restoration is opt-in and no-op by default

The system SHALL build a scope context from each entry (`EventContext.From` with the entry's
`TenantGuid` and `Headers`, plus the entry's `CorrelationGuid`) and run the publish inside
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
  `CorrelationGuid` on every publish

#### Scenario: Message-queue delivery path

- **Given** a `DistributedEventBus` transport subscription and a registered `IEventScopeAccessor`
- **When** a message is delivered and handlers run
- **Then** the pipeline runs inside `IEventScopeAccessor.RunWithScopeAsync` with the rebuilt
  `EventContext`, the accessor resolved from the message's DI scope (or the bus's provider), falling back
  to `NullEventScopeAccessor` when none is registered (SH-M117; `DistributedEventBus` did not consume
  `IEventScopeAccessor` at all, so scope restoration applied to the outbox path only)

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
`ClaimedAt = now` before returning them. `MarkPublishedAsync`, `MarkFailedAsync`, `CleanupAsync` and
`GetAll` SHALL take the same lock, and `now` (for `ClaimedAt`, `PublishedAt` and the staleness test) SHALL come from the optional `IDateTimeProvider` passed to the constructor, else the
system clock.

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

#### Scenario: Marking concurrently with a claim

- **Given** a processor marking an entry while another caller runs `GetPendingAsync` or `CleanupAsync`
- **When** both touch the same entry
- **Then** they run one after the other under the claim lock, so no entry's status, `Attempts` or
  `ClaimedAt` is torn (SH-L122; `Mark*`, `CleanupAsync` and `GetAll` ran outside the lock)

#### Scenario: Staleness judged by a substituted clock

- **Given** an `InMemoryOutboxStore` constructed with an `IDateTimeProvider` and an entry claimed at that
  clock's time
- **When** the clock advances past the stale-claim timeout and `GetPendingAsync` runs
- **Then** the entry is reclaimed, and `ClaimedAt`/`PublishedAt` carry that clock's times (SH-L123; the
  store read `DateTime.UtcNow`, so a test or a host clock could not drive it)

### Requirement: Outbox DI wiring unwraps the decorator so the processor publishes through the real bus

The system SHALL, in **either** `AddOutbox` overload, register `OutboxOptions`, the store, an
`OutboxProcessor` whose publisher is the resolved `IEventBus` — unwrapped to `OutboxEventBus.Inner` when
the resolved bus is an `OutboxEventBus` — and an `OutboxProcessorHostedService`; and `AddOutboxEventBus`
SHALL replace the existing `IEventBus` registration with an `OutboxEventBus` wrapping it, preserving the
original lifetime.

#### Scenario: A store the container cannot activate is supplied by a factory

- **Given** `AddOutbox(sp => new SqlOutboxStore(settings, connectorType))` — the factory overload
- **When** the registrations are built
- **Then** the factory is registered as the `IOutboxStore` source and the processor plus hosted service are
  wired exactly as the generic overload wires them, both paths sharing one `AddOutboxProcessor` helper so the
  decorator-unwrapping cannot diverge between them
- **And** a null factory is rejected with `ArgumentNullException`

#### Scenario: Why the generic overload is not sufficient on its own

- **Given** a SQL-backed outbox store parameterised by settings and a connector type chosen from
  configuration
- **When** a consumer tries `AddOutbox<TStore>()`
- **Then** container activation cannot supply those constructor arguments, because they are not themselves
  registered services — which without the factory overload forces the consumer to re-implement the processor
  and hosted-service registrations by hand and re-derive the decorator-unwrapping

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
- **Then** the exception propagates with its type unchanged although the domain event is already
  durably appended — the append is not rolled back — and carries
  `Data[EventStoreEventBus.UnpublishedEventsKey]` holding the appended-but-unpublished events: that one
  event for `AppendAsync`, and for `AppendRangeAsync` the failing event through the end of the list
  (SH-M122; the exception said neither, so a caller could not republish exactly what was missing)

#### Scenario: Null constructor arguments

- **Given** a null inner store or null bus
- **When** `EventStoreEventBus` is constructed
- **Then** `ArgumentNullException` is thrown

### Requirement: Domain-event wrapping preserves original identity and time

The system SHALL, in the `DomainEventPublished(DomainEvent)` constructor, copy `AggregateGuid`,
`Version`, `EventType`, `EventData`, `Metadata` and `UserGuid`, and overwrite the `EventBase` defaults
with the domain event's `OccurredAt` and `EventGuid`; `Source` SHALL always be `"event-sourcing"`.

#### Scenario: Wrapping a historical domain event

- **Given** a domain event with `OccurredAt` two years ago and a known `EventGuid`
- **When** it is wrapped
- **Then** the wrapper reports that same `EventGuid` and `OccurredAt`, not `Guid.NewGuid()` and now

#### Scenario: Null domain event

- **Given** a null domain event
- **When** the constructor is invoked
- **Then** `ArgumentNullException` is thrown

#### Scenario: Deserialization constructor

- **Given** the parameterless constructor is used (for deserialization)
- **When** the instance is created
- **Then** `EventGuid`/`OccurredAt` take the `EventBase` defaults and `DomainEventType`/`EventData`
  are `string.Empty` until set (SH-L125; they were declared `null!` and remained null until set)

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
- **Then** every event is dropped as a duplicate, because the wrapper preserves the original `EventGuid`

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
to **every** currently registered subscriber, a handler exception stopping no other subscriber and
being reported through `InMemoryConsumer.OnHandlerError` (a throwing hook swallowed). Disposing an
`InMemoryConsumer` SHALL remove every channel subscriber it registered.

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
- **Then** the exception is passed to `OnHandlerError`, the second subscriber still receives the
  message, and the dispatch loop continues (SH-M109; the exception was discarded unobserved)

#### Scenario: Consumer disposed while its subscriptions are registered

- **Given** an `InMemoryConsumer` with subscriptions on a shared channel
- **When** the consumer is disposed and a message is then sent to that destination
- **Then** its handlers no longer receive it — `Dispose` removed the channel subscribers it registered
  (SH-M111; they stayed on the shared channel, so a disposed consumer kept receiving and its
  ack/reject calls then threw `ObjectDisposedException`)

#### Scenario: Pull and push mixed on one destination

- **Given** a subscriber added on a destination and a concurrent `ReadAsync` caller on the same
  destination
- **When** a message arrives
- **Then** the dispatch loop's `ReadAllAsync` and the pull read compete for it — only one observes the
  message; the two modes are mutually exclusive by contract

#### Scenario: Last subscriber removed then re-added

- **Given** the only subscription is disposed, so `RemoveSubscriber` cancels the dispatch CTS and the
  stopping dispatch loop disposes it
- **When** a new subscriber is added for that destination
- **Then** a fresh dispatch loop is started under the lock (SH-L130; `RemoveSubscriber` also disposed the
  token source while the loop could still be reading its token)

### Requirement: Delayed send is in-memory only and best-effort

The system SHALL, in the in-memory producer, when `QueueMessage.Delay` has a value, throw
`OperationCanceledException` if the caller's token is already cancelled, otherwise schedule the enqueue
on a detached task that runs on the producer's lifetime (cancelled by the producer's `Dispose`, not by
the caller's token) and return a completed `SendAsync` immediately, observing but discarding any fault
of that detached task. `MqttProducer.SendAsync` and `RedisProducer.SendAsync` SHALL throw
`NotSupportedException` for a message with a `Delay`.

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

#### Scenario: Caller's token and the delayed send

- **Given** a message with a `Delay`
- **When** `SendAsync` is called with a token that is already cancelled, or with a token cancelled
  after the call returned
- **Then** the first throws `OperationCanceledException` and nothing is scheduled; the second still
  enqueues after the delay, and only disposing the producer cancels the pending send (SH-M112; the
  detached send ran on the caller's token, and a call whose token was already cancelled returned
  quietly)

#### Scenario: Delay on the MQTT or Redis transport

- **Given** a `QueueMessage` with `Delay` set
- **When** it is sent through `MqttProducer.SendAsync` or `RedisProducer.SendAsync`
- **Then** `NotSupportedException` is thrown and nothing is published (SH-M113, decided by the user:
  refuse loudly; the delay was ignored and the message published immediately)

#### Scenario: Non-delayed send

- **Given** a message with no `Delay`
- **When** `SendAsync` is awaited
- **Then** the write to the channel is awaited inline and failures propagate to the caller

### Requirement: Transports honour message and consumer options or refuse them

The system SHALL honour, or refuse through the static `Birko.MessageQueue.TransportSupport` with a
`NotSupportedException` naming the option and the transport at the call that asked for it (TASK-174's
refusals, lifted by TASK-668 where a transport honours the option):

- `QueueMessage.TimeToLive`, counted from `CreatedAt`: honoured by Redis (a `ttl_ms` field, expired entries
  acknowledged and skipped) and in-memory (an expired message is dropped at dispatch, measured on
  `InMemoryMessageQueueOptions.Clock`); on MQTT sent as the MQTT 5 message expiry interval when the client's
  connection is MQTT 5 (`MqttSettings.ProtocolVersion = V500`), refused on MQTT 3.1.1 and refused unless it is a
  whole number of seconds from 1 (the interval counts whole seconds, and 0 means "never expires").
- `ConsumerOptions.PrefetchCount` (at least 1, else `ArgumentOutOfRangeException`): on Redis an explicit value
  is the read batch, i.e. the entries the subscription holds delivered-but-unacknowledged in the stream's
  pending list (the default 1 leaves `RedisStreamSettings.ReadCount` in charge); on in-memory it is the number of
  the subscription's handlers in flight at once (the default 1 is one at a time); refused on MQTT, whose receive
  maximum is a per-connection CONNECT property, not per subscription.
- a non-zero `QueueMessage.Priority`: refused on every current transport (decided by the user 2026-10-18: kept
  for a transport that can order by it, such as RabbitMQ).

The `ReplyTo` and `GroupId` message headers SHALL be carried on the wire with no built-in semantics.

#### Scenario: Time-to-live

- **Given** a `QueueMessage` with `TimeToLive` set
- **When** it is sent through `InMemoryProducer` and its TTL passes before dispatch
- **Then** it is delivered to no subscriber, while a message within its TTL is delivered (TASK-668)
- **And** through `RedisProducer` it is sent with a `ttl_ms` field and skipped once expired; through `MqttProducer`
  on an MQTT 5 connection it carries `MessageExpiryInterval` in seconds; on MQTT 3.1.1, or for a TTL under a
  second or not in whole seconds, `NotSupportedException` is thrown and nothing is published (SH-L108)

#### Scenario: Priority on any transport

- **Given** a `QueueMessage` with a non-zero `Priority`
- **When** it is sent through the in-memory, MQTT or Redis producer
- **Then** `NotSupportedException` is thrown and nothing is sent; `Priority = 0` sends normally (SH-L109,
  decided by the user: refused until a transport that orders by priority exists)

#### Scenario: Prefetch count

- **Given** `ConsumerOptions` with `PrefetchCount = 3`
- **When** `SubscribeAsync` is called on the Redis consumer of a consumer group, or the in-memory consumer
- **Then** the Redis subscription holds at most 3 entries pending at once (measured live with `ReadCount = 5`),
  and the in-memory subscription runs at most 3 handlers at once (TASK-668)
- **And** on the MQTT consumer `NotSupportedException` is thrown and no subscription is created (SH-L110); a
  `PrefetchCount` below 1 is `ArgumentOutOfRangeException` on Redis and in-memory

#### Scenario: Reply-to and group headers

- **Given** a message whose `Headers.ReplyTo` or `Headers.GroupId` is set
- **When** it is sent and received on any of the three transports
- **Then** the header values travel with the message's headers (on MQTT, only as MQTT5 user properties),
  and no transport acts on them (SH-L111; unchanged — documented as carried-only)

### Requirement: Acknowledgment semantics differ per transport

The system SHALL implement `ConsumerOptions.AckMode` per backend: the in-memory consumer tracks
manual-ack messages in a local dictionary keyed by message id, keeping a failed handler's entry
pending; the MQTT consumer acknowledges at the protocol level (QoS) only, refusing what it cannot honour
with `NotSupportedException`; the Redis consumer performs `XACK` — of every stream entry id the message
id was delivered as — and only when a consumer group is in use.

#### Scenario: In-memory manual ack

- **Given** an in-memory subscription with `AckMode = ManualAck`
- **When** the handler completes and `AcknowledgeAsync(message.Guid)` is called
- **Then** the pending entry is removed; nothing else in the transport depends on the ack

#### Scenario: In-memory manual ack, handler throws

- **Given** the same subscription and a handler that throws
- **When** the wrapped handler catches, reports the failure to `OnHandlerError` and rethrows
- **Then** the entry stays pending, so a subsequent `RejectAsync(message.Guid, requeue: true)` writes
  the message back to its destination and it is redelivered (SH-M110; the wrapper removed the pending
  entry, so the requeue found nothing and the message was lost)

#### Scenario: In-memory reject with requeue after a successful handler

- **Given** a manual-ack message still tracked as pending
- **When** `RejectAsync(id, requeue: true)` is called
- **Then** the message is written back to its originating destination and redelivered with its
  `x-delivery-count` header incremented (a missing header counting as 1, so the first redelivery
  carries 2) (SH-L113; the header was never written, so a redelivery looked like a first delivery)

#### Scenario: In-memory reject without requeue

- **Given** a pending manual-ack message
- **When** `RejectAsync(id, requeue: false)` is called
- **Then** the tracking entry is dropped and the message is discarded

#### Scenario: MQTT acknowledge

- **Given** an MQTT subscription
- **When** `AcknowledgeAsync` or `RejectAsync(requeue: false)` is called
- **Then** both complete successfully and do nothing — acknowledgement is the protocol's (QoS)

#### Scenario: MQTT asked for what it cannot honour

- **Given** an `MqttConsumer`
- **When** `SubscribeAsync` is called with `ConsumerOptions` carrying `AckMode = ManualAck`,
  `FromBeginning = true` or a `GroupId`, or `RejectAsync(requeue: true)` is called
- **Then** `NotSupportedException` is thrown (SH-M107, decided by the user: refuse loudly; those options
  were silently ignored and the requeue reported success while nothing was redelivered); a non-default
  `PrefetchCount` is refused the same way (SH-L110, kept at TASK-668: MQTT's receive maximum is per connection)

#### Scenario: Redis auto-ack in a consumer group

- **Given** a Redis subscription with a consumer group and `AckMode = AutoAck`
- **When** the handler completes successfully
- **Then** `XACK` removes the entry from the Pending Entries List

#### Scenario: Redis manual ack of a message delivered as two entries

- **Given** a consumer-group subscription with `AckMode = ManualAck` and one message id published twice,
  so it is delivered as two stream entries
- **When** `AcknowledgeAsync(id)` or `RejectAsync(id, requeue: false)` is called
- **Then** both entry ids are `XACK`ed and none stays in the Pending Entries List (SH-M104; the later
  delivery replaced the tracked entry id, so the earlier entry sat in the PEL forever)

#### Scenario: Redis subscription without a consumer group

- **Given** `RedisStreamSettings.ConsumerGroup` null and no `ConsumerOptions.GroupId`
- **When** messages are consumed via `XREAD`
- **Then** no manual-ack tracking is registered and `AcknowledgeAsync`/`RejectAsync` do nothing,
  because both require a non-null `ConsumerGroup` on the tracked entry

### Requirement: Redis Streams delivery, failure retention and reclaim

The system SHALL poll a stream with `XREADGROUP` (consumer group present) or `XREAD` (otherwise),
report a failed handler through `RedisConsumer.OnHandlerError` (in both modes, a throwing hook
swallowed), leave a failed handler's entry unacknowledged in the Pending Entries List, and — when a
consumer group is used and `PendingRetryMilliseconds > 0` — reclaim and reprocess idle pending entries
via `XAUTOCLAIM` on each empty poll and, after a non-empty batch, once per `PendingRetryMilliseconds`.
An auto-created consumer group SHALL start at `"$"` (new messages only) unless
`ConsumerOptions.FromBeginning`, then at `"0-0"`; an existing group keeps its position. Group creation
and the `FromBeginning` pending drain SHALL be retried every 2 seconds on a `RedisException`. Without a
consumer group there is no PEL, so delivery is at-most-once, and the read position is the consumer's own
`LastReadId`, which starts at `"0-0"` when `FromBeginning` is set and otherwise at the id of the stream's
newest entry at the first poll (`"0-0"` for an empty or missing stream) — never `"$"` — and advances past
every entry a read returned, including one that is unparseable or expired.

#### Scenario: Subscription with no consumer group and default options

- **Given** `RedisStreamSettings.ConsumerGroup` null, no `ConsumerOptions.GroupId`, and
  `ConsumerOptions.FromBeginning` at its default false
- **When** the poll loop runs and messages are produced to the stream after it started
- **Then** the first poll resolves the start position to the newest entry's id (`XREVRANGE … COUNT 1`),
  entries already in the stream are not delivered, and every message produced afterwards is (SH-H020,
  fixed by TASK-318 — the position used to be `"$"`, which StackExchange.Redis refuses for `XREAD` with an
  `InvalidOperationException`, so every poll failed into the internal `PollError` and nothing was delivered)

#### Scenario: An unparseable entry without a consumer group

- **Given** a subscription with no consumer group and a stream entry with neither a `message` nor a `body`
  field
- **When** a read returns it
- **Then** it is skipped and `LastReadId` moves past it, so the next read starts after it rather than
  returning it again

#### Scenario: Handler fails under a consumer group

- **Given** a consumer-group subscription and a handler that throws
- **When** the entry is processed
- **Then** the failure reaches `OnHandlerError`, no `XACK` is issued, the entry remains in the PEL, and
  after `PendingRetryMilliseconds` idle time the reclaim pass reprocesses it (SH-M100; the failure was
  swallowed unobserved)

#### Scenario: Handler fails without a consumer group

- **Given** a subscription with no consumer group and a handler that throws
- **When** the entry is processed
- **Then** the failure reaches `OnHandlerError` and the read position moves on — there is no PEL, so the
  message is not redelivered (at-most-once) (SH-M100; the failure was swallowed, so the loss left no
  trace)

#### Scenario: A failed entry on a busy stream

- **Given** a consumer-group subscription whose stream always has new entries, and one entry whose
  handler failed
- **When** `PendingRetryMilliseconds` has elapsed since the last reclaim
- **Then** the reclaim runs after the next non-empty batch and the failed entry is redelivered
  (SH-M101; the reclaim ran only after an empty poll, so on a busy stream it never ran)

#### Scenario: A reclaimed entry carries its delivery count

- **Given** a consumer-group entry whose handler failed once and which the reclaim pass picks up
- **When** it is redelivered
- **Then** the message's `x-delivery-count` header is the entry's delivery count from the Pending
  Entries List (`XPENDING`), 2 for the first redelivery (SH-L113; nothing wrote the header, so every
  delivery looked like the first)

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
- **Then** the group starts at `"$"` and only entries added afterwards are delivered; with
  `FromBeginning = true` it starts at `"0-0"` and the retained stream is delivered (SH-M102, decided by
  the user; `EnsureConsumerGroupAsync` always created the group at `"0-0"`, replaying the whole stream
  against the option's own doc comment)

#### Scenario: Redis unreachable at subscription setup

- **Given** group creation or the `FromBeginning` pending drain throws a `RedisException`
- **When** the poll task starts
- **Then** the setup is retried every 2 seconds until it succeeds, and the subscription stays active
  (SH-M103; the setup ran outside the loop's retry, so a connection blip at subscribe time ended the
  subscription for good)

#### Scenario: Consumer group already exists

- **Given** `AutoCreateConsumerGroup = true` and an existing group
- **When** the loop calls `StreamCreateConsumerGroupAsync`
- **Then** the `BUSYGROUP` server error (matched by message text) is swallowed and polling proceeds

#### Scenario: Message TTL exceeded

- **Given** an entry carrying `ttl_ms` and a `CreatedAt` older than that TTL, in a consumer group
- **When** the entry is processed
- **Then** the handler is not invoked and the entry is acknowledged and skipped

#### Scenario: Old foreign entry with a TTL and no created_at

- **Given** an entry written by a foreign producer with `body` and `ttl_ms` but no `created_at` field,
  appended longer ago than that TTL
- **When** the entry is processed
- **Then** `CreatedAt` is the stream id's millisecond part (when Redis appended it), so the entry is
  expired and skipped (SH-L103; `CreatedAt` was the parse time, so such an entry never expired)

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
public `RedisConsumer.PollError` event and keep polling after a 2-second backoff, retry after 2 seconds
on a `RedisException`, and on a fault that terminates the loop report it and remove the subscription
so `RedisSubscription.IsActive` becomes false. `RedisConsumer.SubscribeAsync` SHALL return once the
subscription is registered; its poll loop, and consumer-group creation on the first poll, SHALL start
in the background.

#### Scenario: Application observes poll faults

- **Given** an application subscribed to `RedisConsumer.PollError`
- **When** a poll iteration faults or the loop terminates
- **Then** the subscriber receives the exception (SH-L102; the event was internal, so an application
  could not observe a subscription that died or an entry that failed every poll)

#### Scenario: Subscribing does not wait for setup

- **Given** a `RedisConsumer`
- **When** `SubscribeAsync` is called
- **Then** it returns a completed task with the registered subscription before the consumer group is
  created, and setup proceeds in the poll loop (SH-L104; the method was declared `async` with nothing to
  await)

#### Scenario: One poisoned entry

- **Given** an entry whose processing throws a serializer exception
- **When** the poll iteration faults
- **Then** `PollError` fires, the loop backs off 2 seconds and continues polling

#### Scenario: Connection lost

- **Given** the Redis server becomes unreachable, raising `RedisException`
- **When** the poll iteration fails
- **Then** the loop waits 2 seconds and retries indefinitely without raising `PollError`

#### Scenario: Consumer-group creation fails fatally

- **Given** subscription setup throws an error that is not a `RedisException` before the loop starts
- **When** the poll task terminates
- **Then** `PollError` fires, the subscription is removed, and `IsActive` reports false rather than
  claiming an alive subscription that processes nothing (CR-M207; a `RedisException` at setup is retried
  instead, SH-M103)

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
even `body` is missing. When `created_at` is absent, `CreatedAt` SHALL be the stream entry id's
millisecond part (the Redis append time).

#### Scenario: Round-trip through the Birko producer

- **Given** a `QueueMessage` with id, headers, payload type and priority
- **When** it is sent and consumed
- **Then** all of those survive, because the whole message was serialized into `message`

#### Scenario: Entry written by a foreign producer

- **Given** a stream entry with only a `body` field
- **When** it is parsed
- **Then** a `QueueMessage` with that body and default id/headers is produced, whose `CreatedAt` is the
  stream id's millisecond part (SH-L103; it was the parse time)

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
(best-effort, swallowing failures), and dispatch each received message to every handler of every
registered filter that matches, handing each handler its own `QueueMessage.Copy()` (a public method
that shares no mutable state, headers included) and isolating handler exceptions behind the optional
`OnHandlerError` hook.

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
- **Then** handlers are kept per filter and per subscription, so both handlers receive every matching
  message, as on the in-memory transport (SH-M106; the handler dictionary entry was keyed by filter and
  overwritten, so the earlier handler stopped receiving while its subscription still reported active)

#### Scenario: Handlers receive no cancellation token

- **Given** any MQTT subscription
- **When** a message is dispatched
- **Then** the handler is invoked with `CancellationToken.None`

#### Scenario: One handler changes the message

- **Given** two handlers matching one received message, the first of which changes the message's body
  or a header
- **When** the message is dispatched
- **Then** the second handler receives the message as received, because each handler gets its own
  `QueueMessage.Copy()` (SH-L105; all handlers shared one instance, so one handler's change reached the
  next)

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

- **Given** a producer-side `QueueMessage.Guid`
- **When** the message is received
- **Then** the consumer constructs a new `QueueMessage` with a freshly generated `Guid`, and `CreatedAt`
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
cancelled, or `MaxReconnectAttempts` failures (0 meaning unlimited). Each `Connected` and `Disconnected`
subscriber SHALL be awaited in turn and isolated, so a throwing one stops neither the others nor the
reconnect loop.

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

#### Scenario: A Disconnected subscriber throws

- **Given** `AutoReconnect = true` and two `Disconnected` subscribers, the first of which throws
- **When** the connection drops
- **Then** the second subscriber is still awaited and the reconnect loop starts (SH-M108; the exception
  propagated into the client's event and the reconnect loop never started, leaving the client offline,
  and a multicast invoke awaited only the last subscriber)

### Requirement: MQTT subscription disposal removes its handler and unsubscribes the last one at the broker

The system SHALL, on `MqttSubscription.UnsubscribeAsync` or `Dispose`, flip `IsActive` false and remove
that subscription's handler from the consumer; only when it was the filter's last handler SHALL it
issue the broker UNSUBSCRIBE — awaited by `UnsubscribeAsync`, fired on a detached task swallowing
failures by `Dispose`.

#### Scenario: Unsubscribe awaited

- **Given** the only active MQTT subscription for a filter
- **When** `UnsubscribeAsync` is awaited
- **Then** its handler is removed, the broker UNSUBSCRIBE completes and `IsActive` is false

#### Scenario: One of two subscriptions on a filter released

- **Given** two subscriptions on `"sensors/#"`
- **When** one is unsubscribed or disposed
- **Then** no broker UNSUBSCRIBE is issued and the remaining subscription keeps receiving (SH-M106; the
  release unsubscribed the filter at the broker for both)

#### Scenario: Disposed subscription and a later reconnect

- **Given** a subscription disposed while other subscriptions remain, then a reconnect
- **When** `ResubscribeAllAsync` replays the consumer's registered filters
- **Then** the disposed subscription's handler is gone, so it receives nothing and a filter with no
  handler left is not re-subscribed (SH-M105; the handler stayed registered, so the filter was
  re-subscribed and the released handler started receiving again)

#### Scenario: Unsubscribe twice

- **Given** an already-unsubscribed subscription, or two callers releasing it concurrently
- **When** `UnsubscribeAsync` or `Dispose` is called again
- **Then** it returns immediately without issuing another broker call — the release is claimed with
  `Interlocked`, so exactly one caller tears down (SH-L115; a non-atomic check let two concurrent
  callers both release)

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
while sharing `ClientCertificate` and `LastWill` by reference; `LoadFrom(Settings)` with any other
settings type SHALL fall back to the base `LoadFrom`.

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
- **Then** it falls back to the base `LoadFrom`, so what that instance carries (location, name, and for
  password or remote settings the credentials) is copied and the MQTT-specific options are left as
  they were (SH-L106; nothing was copied at all and the call reported success)

#### Scenario: Host constructor without credentials

- **Given** `new MqttSettings("broker.local")` with no username or password
- **When** `Name`, `UserName` and `Password` are read
- **Then** each is `string.Empty`, never null (SH-L107; the constructor passed `null!` for the
  non-nullable base members)

### Requirement: DI registration surface for the event bus

The system SHALL register `InProcessEventBus` as a singleton `IEventBus` built from the root provider
plus all registered `IEventPipelineBehavior` and `IEventEnricher` services, register handlers as
transient `IEventHandler<TEvent>`, and register behaviours/enrichers as singletons in call order.

#### Scenario: Standard wiring

- **Given** `AddEventBus()` then `AddEventHandler<OrderPlaced, OrderPlacedHandler>()`
- **When** `IEventBus` is resolved and an `OrderPlaced` is published
- **Then** the handler is resolved from a scope opened for that publish and invoked

#### Scenario: Handler with a scoped dependency

- **Given** a handler registered transiently that depends on a scoped service, in a root built with
  `ValidateScopes = true`
- **When** an event is published, directly or through `AddOutbox` + `OutboxProcessor`
- **Then** the handler runs with a scoped instance owned by that publish's scope; two publishes see two
  instances, each disposed when its publish ends (TASK-565 — before it, resolution from the root threw
  under `ValidateScopes` and otherwise shared one root-owned instance across every dispatch)

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
