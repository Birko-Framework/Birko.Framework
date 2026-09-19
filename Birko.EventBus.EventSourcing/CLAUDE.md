# Birko.EventBus.EventSourcing

## Overview
Integration layer between Birko.EventBus and Birko.Data.EventSourcing. Publishes domain events to the event bus after event store append, and provides replay for projection rebuilding.

## Project Location
- **Directory:** `Birko.EventBus.EventSourcing/`
- **Type:** Shared Project (.shproj / .projitems)
- **Namespace:** `Birko.EventBus.EventSourcing`

## Components

| File | Description |
|------|-------------|
| DomainEventPublished.cs | EventBase record wrapping domain event data (AggregateId, Version, DomainEventType, EventData, Metadata, UserId) |
| EventStoreEventBus.cs | IAsyncEventStore decorator — delegates all operations to inner store, publishes DomainEventPublished after Append/AppendRange |
| EventReplayService.cs | Replays events from store through bus: ReplayAggregateAsync, ReplayFromVersionAsync, ReplayAllFromAsync |

## Important Notes
- **IEvent name conflict:** `Birko.EventBus.IEvent` and `Birko.Data.EventSourcing.Events.IEvent` share the same name. Files use `DomainEvent = Birko.Data.EventSourcing.Events.IEvent` alias and fully qualify `Birko.EventBus.IEventBus`
- DomainEventPublished.Source is always "event-sourcing"
- EventStoreEventBus publishes AFTER successful append (not before)

## Dependencies
- Birko.EventBus — IEventBus, EventBase, IEventHandler
- Birko.Data.EventSourcing — IAsyncEventStore, IEvent, DomainEvent

## Maintenance
- When adding new files, update the .projitems file
- If IAsyncEventStore interface changes, update EventStoreEventBus accordingly
