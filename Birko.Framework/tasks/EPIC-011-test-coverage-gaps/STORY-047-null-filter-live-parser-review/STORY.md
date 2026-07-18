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
**oracle-based matrix** tests were added (no-op pass when the backend is absent):
- `MongoFilterMatrixLiveTests` → `BIRKO_MONGO_HOST`
- `CosmosFilterMatrixLiveTests` → `BIRKO_COSMOS_CONNECTION`
- `RavenFilterMatrixLiveTests` → `BIRKO_RAVEN_URL`

Each seeds a fixed dataset, reads it back, and for every shape compares `store.ReadAsync(expr)` against
`expr.Compile()` over the read-back set (so serialization round-trips are neutralised and only genuine
translation divergences surface). They have **not yet been executed against real backends** — that is this story.

## Filter-shape matrix

The live tests and the in-process parser tests share this catalogue:

- **Comparisons / ranges:** `==`, `!=`, `<`, `<=`, `>`, `>=`; numeric ranges; `decimal` compare.
- **Null:** `== null`, `!= null`, `HasValue` (provider null-vs-missing nuances noted below).
- **Booleans:** bare bool `x.Active`, constant `x => true`/`false`, `&&`/`||`, bitwise `&`/`|`, `!`.
- **Nested boolean grouping:** `(a || b) && (c || d)`, `(a && b) || (c && d)`, `!(a && b)` (De Morgan),
  deep nesting, `A && !(B) && (C || D)` — precedence must be preserved, not flattened.
- **Strings:** `StartsWith`, `EndsWith`, `Contains`, `ToLower()/ToUpper()` comparison (case-insensitivity).
- **Collections:** IN via `collection.Contains(x.Member)`, array membership, nested `x.Items.Any(i => …)`.
- **Serialized types:** `enum` equality, `Guid` equality, `DateTime` range + `.Date`.
- **Structure:** nested member path `x.Address.City == …`, interface/base-typed `Convert(param, iface)`.

## Behaviour / acceptance criteria

- Stand up MongoDB + Cosmos (emulator) + RavenDB (containers/emulator); set the three env vars and run the
  `*FilterMatrixLiveTests` green (0 divergences).
- Record any provider-specific nuances found — especially:
  - Cosmos: LINQ `x.F == null` translation (`c.F = null` matches nothing in Cosmos SQL vs `IS_NULL(c.F)`);
    `.Date`, `ToLower`, nested `Any` support.
  - Mongo: `{F: null}` matches null **or** missing; `$ne: null` excludes both; enum int-vs-string storage.
  - Raven: indexing lag (the test polls), enum stored as string, null/missing handling.
- Decide the skip mechanism: adopt `Xunit.SkippableFact` (or Testcontainers, per EPIC-011) so these report
  as **Skipped** instead of a no-op **Pass** when infra is absent.

## Findings

- **SQL parser bug found + fixed (this session):** `AbstractConnectorBase.AppendConditionTo` applied
  `Condition.IsNot` only on the leaf path — a negated **group** (`!(a && b)`, `!(a || b)`, or a negated
  comparison that became a single-child sub-group) rendered as `(a AND b)` with the `NOT` **silently
  dropped**, so the filter matched the OPPOSITE rows. Fixed in `AppendSubConditionsTo` (prefix `NOT` +
  parenthesise negated groups). Caught by the new `grpAndOr`/`deMorgan`/`mixedNot` cases in
  `SqlExpressionParityTests`. ElasticSearch nests correctly (verified structurally); Mongo/Cosmos/Raven
  pending the live run.

## Provenance

Follows the ElasticSearch parser parity fix and the SQL/ES parity tests (this session). See
`Birko.Data.ElasticSearch` CLAUDE.md § "Filter translation".
