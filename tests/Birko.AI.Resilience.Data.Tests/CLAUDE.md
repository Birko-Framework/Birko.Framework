# Birko.AI.Resilience.Data.Tests

## Overview
Tests of `StoreCircuitBreakerStore` and `StoreUsageRepository` (TASK-530), each run on two backends through one
abstract class: `AsyncInMemoryStore` and `AsyncSQLiteStore` (a real database file per test; a new store object per
call, as a second process would have). The breaker tests drive the real `ProviderCircuitBreaker`: an open circuit
survives a new breaker, a second save replaces the row, a reset is persisted. The usage tests pin half-open windows,
the provider filter, grouping, project totals, an empty store, the budget check, and the refusal of a store that does
not aggregate.

## Dependencies
xUnit, FluentAssertions; imports Birko.AI.Resilience(.Data), Birko.Data.InMemory, Birko.Data.SQL.SqLite and their
dependencies.

## Maintenance
See [CLAUDE-maintenance.md](../../Birko.Framework/CLAUDE-maintenance.md).
