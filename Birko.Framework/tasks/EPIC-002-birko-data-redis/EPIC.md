---
id: EPIC-002
status: planned
created: 2026-05-28
owner: ai
affects: [Birko.Data.Redis]
---

# Birko.Data.Redis

## Area of concern

Add Redis as a Birko data store backend. Hash-based storage (`HSET`/`HGET`) for CRUD with hybrid aggregation: native `FT.AGGREGATE` when RediSearch (Redis Stack) is available, LINQ fallback otherwise.

## Success criteria

- `Birko.Data.Redis` exists as a sibling shared project, registered in `.slnx` / `.code-workspace` / aggregator
- Implements `IAsyncStore<T>`, `IAsyncBulkStore<T>`, `IAsyncAggregatableStore<T>`
- Aggregation gracefully falls back to LINQ when RediSearch is unavailable
- xUnit + FluentAssertions tests passing
