---
id: STORY-047
parent: EPIC-011
status: planned
created: 2026-07-18
owner: ai
affects: [Birko.Data.MongoDB.Tests, Birko.Data.CosmosDB.Tests, Birko.Data.RavenDB.Tests]
---

# Review filter-parser behaviour on live document databases

## User story

As a maintainer, I want the filter-parser behaviour (starting with `x.Field == null` / `!= null` / `HasValue`)
verified against **live** MongoDB, Cosmos DB, and RavenDB, so the native-LINQ / driver translation is *proven*
rather than assumed.

## Background

The SQL (`DataBase.ParseConditionExpression`) and ElasticSearch (`ElasticSearch.ParseExpression`) filter
parsers are hand-rolled and unit-tested in-process:
- `Birko.Data.SQL.SqLite.Tests.SqlExpressionParityTests` — matrix vs compiled-delegate oracle (SQLite).
- `Birko.Data.ElasticSearch.Tests.ExpressionDivergenceTests` — NEST query structure per case.

Mongo / Cosmos / Raven have **no** hand-rolled parser — they forward the raw `Expression` to the
driver / LINQ provider, so their behaviour can only be verified against a running backend. Env-var-gated
live tests were added (no-op pass when the backend is absent):
- `MongoNullFilterLiveTests` → `BIRKO_MONGO_HOST`
- `CosmosNullFilterLiveTests` → `BIRKO_COSMOS_CONNECTION`
- `RavenNullFilterLiveTests` → `BIRKO_RAVEN_URL`

They have **not yet been executed against real backends** — that is this story.

## Behaviour / acceptance criteria

- Stand up MongoDB + Cosmos (emulator) + RavenDB (containers/emulator); set the three env vars and run the
  `*NullFilterLiveTests` green.
- Confirm `== null` matches the null docs, `!= null` / `HasValue` match only the non-null docs on each backend.
- Record any provider-specific null-vs-missing nuances found — especially:
  - Cosmos: LINQ `x.F == null` translation (`c.F = null` matches nothing in Cosmos SQL vs `IS_NULL(c.F)`).
  - Mongo: `{F: null}` matches null **or** missing; `$ne: null` excludes both.
  - Raven: indexing lag (the test already polls) and null/missing handling.
- Extend the live matrix beyond null to the shapes the SQL/ES work covered (EndsWith, ToLower, IN,
  bitwise `&`/`|`, bare/const bool, StartsWith) to confirm the native providers honour C# semantics.
- Decide the skip mechanism: adopt `Xunit.SkippableFact` (or Testcontainers, per EPIC-011) so these report
  as **Skipped** instead of a no-op **Pass** when infra is absent.

## Provenance

Follows the ElasticSearch parser parity fix and the SQL/ES parity tests (this session). See
`Birko.Data.ElasticSearch` CLAUDE.md § "Filter translation".
