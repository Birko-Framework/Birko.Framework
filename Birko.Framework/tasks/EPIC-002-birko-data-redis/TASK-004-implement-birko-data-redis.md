---
id: TASK-004
parent: EPIC-002
status: todo
priority: P2
assignee: ai
created: 2026-05-28
depends-on: []
blocks: []
pr: null
github-issue: null
jira-key: null
---

# Implement Birko.Data.Redis

## Context

Hash-based Redis data store with hybrid aggregation. Native `FT.AGGREGATE` (server-side GROUP BY/SUM/AVG/MIN/MAX/COUNT, time bucketing via `APPLY`) when RediSearch is available — requires `FT.CREATE` index. Falls back to `AggregateHelper.LinqAggregate()` otherwise (same as JSON/XML/InfluxDB stores).

## Acceptance criteria

- [ ] `Birko.Data.Redis` shared project exists, registered in `Birko.Framework.slnx` / `.code-workspace` / aggregator csproj
- [ ] `RedisStore.cs` extends `AsyncStore<T>` + `AsyncBulkStore<T>` with `*Core` overrides; lazy-init
- [ ] `RedisAggregationHelper.cs` translates `AggregateQuery<T>` to `FT.AGGREGATE` args, parses results
- [ ] `RedisFieldMapper.cs` maps C# property → Redis field name
- [ ] `RedisIndexManager.cs` builds `FT.CREATE` schema from model metadata
- [ ] Falls back to LINQ aggregation when RediSearch is absent
- [ ] Implements `IAsyncAggregatableStore<T>`
- [ ] xUnit + FluentAssertions tests (Testcontainers Redis, with and without RediSearch)
- [ ] CLAUDE.md / README.md / License.md / .gitignore present

## Out of scope

- RedisJSON (separate concern)
- Pub/sub (lives in `Birko.MessageQueue.Redis`)
