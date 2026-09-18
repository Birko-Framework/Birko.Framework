# Birko.Data.EventSourcing.Tests

## Overview
Unit tests for Birko.Data.EventSourcing — the store wrappers that record CRUD as domain events.

## Project Location
`C:\Source\Birko\Framework	ests\Birko.Data.EventSourcing.Tests\`

## Test Framework
xUnit + FluentAssertions

## Scope & conventions
- The inner store under test is `Birko.Data.InMemory` (`InMemoryStore<T>` / `AsyncInMemoryStore<T>`) —
  the canonical test double — so tests exercise the real create → event-append → persist path.
- `IEventStore` / `IAsyncEventStore` are backed by a small in-memory test double defined in the test
  class (no concrete event store ships in the framework yet).
- Test entities implement `AbstractModel` + `IEventSourced`.

## Coverage
- `EventSourcingStoreWrapperTests` — regression tests for CR-C05 / CR-C06: the Created-event
  `AggregateId` must equal the Guid the inner store persists the row under (single, bulk sync, bulk
  async), so `Replay` / `GetHistory` by the persisted Guid find the event.
