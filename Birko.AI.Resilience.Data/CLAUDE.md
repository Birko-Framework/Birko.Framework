# Birko.AI.Resilience.Data

## Overview
Store-backed implementations of `Birko.AI.Resilience`'s two persistence interfaces (TASK-530), over any
`IAsyncBulkStore<T>`, so circuit breaker state and LLM usage persist on whatever backend the consumer already runs.
Before this, both interfaces said "implement per platform" and nothing did: DraCode's `ProviderCircuitBreaker` ran with
no store, so an open circuit closed on every restart.

## Project Location
`C:\Source\Birko\Framework\Birko.AI.Resilience.Data\`

## Components
- `Models/CircuitBreakerStateModel` (table `AiCircuitBreakerStates`): one row per provider. Its `Guid` is a name-based
  Guid of the provider (`GuidFor`), so a save replaces the provider's row.
- `Models/UsageRecordModel` (table `AiUsageRecords`): one row per LLM call.
- `Stores/StoreCircuitBreakerStore` (`ICircuitBreakerStore`): `SaveAsync` is the store's `SaveAsync` by that Guid (an
  entity whose Guid is not stored is created under it, TASK-555). Saves for one provider are serialized by the breaker
  in one process; two processes saving a provider's first state at the same moment can collide on the key, and the
  loser's save throws (the breaker logs it and the next change saves again).
- `Stores/StoreUsageRepository` (`IUsageRepository`): every total is one `AggregateAsync` (Count, Sum of tokens, Sum of
  cost), so the store sums. **A store that is not `IAsyncAggregatableStore<UsageRecordModel>` is refused at
  construction** — summing in memory would read every usage row on each budget check. Every SQL provider, InMemory,
  JSON, XML, the NoSQL stores and the `StoreWrapperBuilder` decorators aggregate. Windows are half-open
  (`from` included, `to` not). `GetUsageByProjectAsync` returns `null` for a project with no call in the window.

```csharp
var breaker = new ProviderCircuitBreaker(store: new StoreCircuitBreakerStore(breakerStore));
await breaker.LoadPersistedStateAsync(ct);   // once, at startup

var costs = new CostTrackingService(config, logger, new StoreUsageRepository(usageStore));
```

The models carry only `[Table]` from `System.ComponentModel.DataAnnotations`, so the project depends on no SQL
package; every property maps by convention. `RecordedAt` is stored as written (a wall clock on SQL, no `[UtcField]`);
`CostTrackingService` writes UTC, so pass UTC windows.

## Dependencies
- Birko.AI.Resilience (`ICircuitBreakerStore`, `IUsageRepository` and their DTOs) and Birko.AI.Contracts
- Birko.Data.Core (`AbstractModel`), Birko.Data.Stores (`IAsyncBulkStore`, `IAsyncAggregatableStore`, `AggregateQuery`)

Its own project so `Birko.AI.Resilience` stays free of any data-store dependency.

## Maintenance
See [CLAUDE-maintenance.md](../Birko.Framework/CLAUDE-maintenance.md). Tests: `tests/Birko.AI.Resilience.Data.Tests`.
