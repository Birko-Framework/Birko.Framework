---
id: TASK-907
parent: null
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: done
# blocked: <reason> — set by /tasks block, never by hand; status stays as it is; /tasks unblock removes it
priority: P3
# assignee — one of: agent (default), unassigned, human, or an installed agent type's name; ai is read as agent, never written
assignee: agent
created: 2026-11-10
depends-on: [TASK-906]
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# Aggregates: string MIN/MAX order, enums stored as text, native Flux

## Context

Found by TASK-906 (2026-11-10):
- **String MIN/MAX order (measured):** over {apple, Banana, éclair, zebra}, MongoDB gives Min "Banana", Max "éclair"
  (binary order); the LINQ oracle gives "apple" / "zebra" because `Comparer.Default` is culture-sensitive (sk-SK here).
  SQL collations, ES and Cosmos likely differ too (not measured). **Decide with the user** whether the oracle should
  compare ordinally (consistent with TASK-873's exact string equality) — Cosmos and ES already refuse string MIN/MAX.
- An enum stored with `[BsonRepresentation(String)]` would take its MongoDB Min/Max by name, not value (not measured).
- InfluxDB no longer aggregates server-side (TASK-906 removed the wrong native path); an exact native Flux path could be
  added if read-everything cost matters.

**Decided by the user (2026-11-10):** string MIN/MAX order is a parameter on the query, defaulting to
`StringComparison.Ordinal` (so `Banana` < `apple`, the same on every machine and culture, consistent with TASK-873's exact
string equality). The `AggregateHelper` oracle honours it; a provider that can match Ordinal does so (SQL through a binary
collation, MongoDB's binary order, Cosmos and Elasticsearch where possible instead of refusing); any other comparison is
honoured in memory and refused by name where a provider cannot sort that way — never answered in the database's own order.

## Acceptance criteria

- [x] Measure string MIN/MAX per provider; get the user's decision on the oracle's string order and record it.
      → Measured 2026-11-10 (`AggregateStringOrderContract`, 32 cases per provider: case, accents, trailing space, ß/ss,
        one word in two cases, nulls, all-null groups, an astral character) against an Ordinal oracle: InMemory, JSON,
        XML, RavenDB store 10 wrong (the oracle used `Comparer.Default`, culture sk-SK); SQLite 22, PostgreSQL 30, MySQL
        30, SQL Server 28 (ties answered differently sync vs async), TimescaleDB 22, MongoDB 22; Elasticsearch and Cosmos
        refused even Ordinal. SQL views 8 of 8 wrong (PG/MySQL `apple` < `Banana`, MySQL ß = ss, SQL Server `a` = `a `,
        U+FF5E < U+1F600). Decision recorded above (the user's).
- [x] Implement it, or refuse by name where a provider cannot match; enums-as-text measured and fixed or refused.
      → `AggregateQuery<T>.StringComparison` (default `Ordinal`, kept by `WithFilter`); `AggregateMath.ValueComparer`;
        one refusal text `AggregateMath.StringOrderNotSupported`; `ThrowIfStringOrderNotOrdinal` refuses any other order
        before any request on every provider but the in-memory ones (OrdinalIgnoreCase too: ties have no single answer).
        Views are always Ordinal. SQL: dialect hook `OrdinalStringExtreme` — PostgreSQL `COLLATE "C"` key with a U+10FFFF
        marker, MySQL MIN/MAX over UTF-16BE bytes, SQL Server `+ NCHAR(0)` under `Latin1_General_100_BIN2` trimmed by
        `DATALENGTH`, SQLite a `birko_ordinal` collation; the migrations `ViewSqlGenerator` renders through the
        connector's dialect and refuses a string MIN/MAX without one (string join keys there become exact too).
        MongoDB `$min`/`$max` and Cosmos compare code points; Elasticsearch a `terms` size-1 aggregation by key (a value
        past `ignore_above` fails the read); RavenDB view reduce `string.CompareOrdinal`. A group holding both a
        character above U+FFFF and one in U+E000–U+FFFF (code-point order ≠ UTF-16 order) is refused by name on MongoDB,
        Elasticsearch and Cosmos DB, stores and views, from two per-group counts. Kept documented (user's decision): a
        value holding U+10FFFF on PostgreSQL, `"a"` vs `"a\0"` on SQL Server.
      → MongoDB enum stored as text: Min of Zero and High was High (names compared), 6 of 6 red; now by value, a value
        still stored as a number included (red 2 of 2 before); an unknown name fails the read; `[Flags]`/`ulong` enums as
        text and a view Min/Max of an enum as text refused by name.
      → Independent review before commit: the SQL renderings checked exact; two silent wrong answers it found (mixed
        astral groups pinned instead of refused; migration views in collation order) fixed, plus numeric enum documents,
        a data-dependent refusal, and empty-string / `"a"` vs `"a\t"` cases added.
      → Tests: `AggregateStringOrderContract` (44 per provider, 12 providers), `Task907AggregateStringOrderTests`,
        `Task907ViewStringOrderTests`, `Task907MigrationViewStringOrderTests` (red 4 of 5), Mongo enum/view, Raven view,
        ES store/view, Cosmos view tests; pins of the old answers inverted on purpose (rule 56): ES `Task878`, SQL
        `Task892`, Cosmos `Task897` store and view.
      → Live: all 119 importer test projects of the changed shared projects green on the working tree (21,799 tests; Redis suites excluded, no Redis host); on a staged HEAD + patch snapshot: Data 335, SQL.View.Migrations 41, SQL.Views 3963, SQL.Tests 1125, SQLite 2410, PostgreSQL 1830, SQL Server 1886, MongoDB 459, Elasticsearch 668, CosmosDB.Views 173, CosmosDB 518 - all green.
      → Consumers: only Symbio's `InMemoryTimeSeriesStore` calls `ComputeAggregate`, on numbers; the new parameter is
        optional.
- [x] Native Flux aggregation decided on cost.
      → Measured on the live container (10 groups, Count + Sum): 10k points ≈ 4.0 s either way; 100k points the store
        aggregate fails at the client's 10 s default timeout, a server-side Flux pivot/group/sum takes 46–49 s; reading
        100k rows takes 45–50 s and ~290 MiB, aggregating them in memory 51–79 ms. The cost is the pivot: every point is
        its own series (per-point `Guid` tag), so native Flux would not be faster. The decision is the user's and moved
        with the timeout to TASK-908 (not implemented here).

## Out of scope

- Null handling in aggregates (TASK-886, TASK-906, done).

## Human test plan

N/A — fully covered by automated tests.

## Implementation plan
