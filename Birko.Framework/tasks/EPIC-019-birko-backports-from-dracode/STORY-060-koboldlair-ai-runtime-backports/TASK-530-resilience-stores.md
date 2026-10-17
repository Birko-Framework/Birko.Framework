---
id: TASK-530
parent: STORY-060
feature: null
status: done
priority: P2
assignee: ai
created: 2026-10-08
depends-on: []
blocks: []
pr: null
github-issue: null
jira-key: null
---

# Birko.AI.Resilience: store implementations for circuit-breaker state and usage

## Context

`ICircuitBreakerStore` and `IUsageRepository` say "implement per platform", and nothing implements either. In DraCode,
`Data/Entities/CircuitBreakerEntity.cs` is dead code, and `DraCode.KoboldLair.Server/Program.cs` constructs
`new ProviderCircuitBreaker(logger: logger)` with no store. Breaker persistence was lost when this code moved to Birko,
while DraCode's EPIC-016 still says it is in the DB. Usage is persisted by `SqlUsageRepository.cs` (171) +
`UsageRecordEntity.cs` (66), which aggregates in memory and adds a per-provider spend filter outside the interface.

Adopted in the consumer by DraCode TASK-128 (`Consumers/DraCode/tasks/EPIC-018-adopt-birko-upstreams/STORY-040`).

## Acceptance criteria

- [x] Implementations on `IAsyncBulkStore<T>` so any backend works (in `Birko.AI.Resilience` or a new `Birko.AI.Resilience.Data`)
- [x] Both interfaces take a `CancellationToken` — breaking on a shared project, so a CHANGELOG entry per CLAUDE-maintenance.md § Breaking changes
- [x] `GetTotalSpendAsync` gains an optional provider filter; totals via `AggregateAsync`, not in memory
- [x] Tests: breaker state survives a new breaker instance; spend per provider and window on InMemory and SQLite

## Human test plan

N/A — library behaviour, covered by unit tests.

## Resolution (2026-10-17)

**New project `Birko.AI.Resilience.Data`**, so `Birko.AI.Resilience` stays free of any data-store dependency:
- `StoreCircuitBreakerStore` over `IAsyncBulkStore<CircuitBreakerStateModel>`. A save is the store's `SaveAsync` under a
  name-based Guid of the provider, which creates an entity whose Guid is not stored (TASK-555), so it upserts: one row
  per provider.
- `StoreUsageRepository` over `IAsyncBulkStore<UsageRecordModel>`. Every total is one `AggregateAsync` (Count, Sum of
  tokens, Sum of cost, grouped by provider where asked). A store that is not `IAsyncAggregatableStore` is **refused at
  construction** rather than summed in memory. Every SQL provider, InMemory, JSON, XML, the NoSQL stores and every
  `StoreWrapperBuilder` decorator aggregate, so in practice nothing is refused.
- The models carry only the BCL `[Table]` (`AiCircuitBreakerStates`, `AiUsageRecords`) and map by convention.

**Interfaces (breaking, CHANGELOG entry with old and new signatures):** both take a `CancellationToken`, and
`GetTotalSpendAsync(from, to, provider = null, ct)` filters by provider. The services pass the token through.
`TrackedLlmProvider` gives the budget check the request's token and records usage with none, because once the call has
been made its spend must be recorded.

**Defect found on the way:** `ProviderCircuitBreaker.Reset` / `ResetAll` cleared memory and never persisted, so with a
store the next `LoadPersistedStateAsync` brought a reset circuit back Open. Now persisted. Red on HEAD's breaker
(`A_reset_is_persisted…`, 2/2: "found CircuitState.Open"), on both backends.

**Tests:** `tests/Birko.AI.Resilience.Data.Tests`, 12 tests × InMemory and SQLite (a file per test, a new store object
per call) = 24 green. Regression: both importers of `Birko.AI.Resilience.projitems` (Resilience 13/13, Resilience.Data
24/24). Adopted by DraCode TASK-128, which replaces its `SqlUsageRepository` and gives its breaker a store.
