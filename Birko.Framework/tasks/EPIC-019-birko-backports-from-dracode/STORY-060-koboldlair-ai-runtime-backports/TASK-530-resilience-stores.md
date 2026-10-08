---
id: TASK-530
parent: STORY-060
feature: null
status: todo
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

- [ ] Implementations on `IAsyncBulkStore<T>` so any backend works (in `Birko.AI.Resilience` or a new `Birko.AI.Resilience.Data`)
- [ ] Both interfaces take a `CancellationToken` — breaking on a shared project, so a CHANGELOG entry per CLAUDE-maintenance.md § Breaking changes
- [ ] `GetTotalSpendAsync` gains an optional provider filter; totals via `AggregateAsync`, not in memory
- [ ] Tests: breaker state survives a new breaker instance; spend per provider and window on InMemory and SQLite

## Human test plan

N/A — library behaviour, covered by unit tests.

## Implementation plan
