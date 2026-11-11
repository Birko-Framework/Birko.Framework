---
id: TASK-909
parent: null
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: done
# blocked: <reason> — set by /tasks block, never by hand; status stays as it is; /tasks unblock removes it
priority: P3
# assignee — one of: agent (default), unassigned, human, or an installed agent type's name; ai is read as agent, never written
assignee: agent
created: 2026-11-10
depends-on: [TASK-907]
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# Aggregates: string ordering of result rows; MongoDB default collation

## Context

Found by TASK-907 (2026-11-10):
- `AggregateHelper.ApplyOrderingAndPaging` orders result rows (group keys, string MIN/MAX values) with `Comparer.Default`,
  culture-sensitive; MongoDB, Elasticsearch and Cosmos DB order their rows through it, while SQL `ORDER BY` uses the
  column collation. **Decide with the user** whether `AggregateQuery.StringComparison` also governs row ordering (default
  Ordinal, as MIN/MAX).
- A MongoDB collection created with a default collation would make `$min`/`$max` follow that collation instead of binary
  order (not measured); `Collation.Simple` on the aggregation would fix it but also changes `$match`.

**Decided by the user (2026-11-11):** yes — `AggregateQuery.StringComparison` (default `Ordinal`) also governs the
ordering of result rows by a string group key or a string MIN/MAX, on every provider (SQL `ORDER BY` through the ordinal
key); a provider that cannot match refuses by name, as for MIN/MAX.

## Acceptance criteria

- [x] Measure both (rule 54); the user's decision on row ordering recorded.
      → Measured 2026-11-11 (56 row-order cases added to `AggregateStringOrderContract`: by a string MIN and by the
        group key, asc/desc, four page windows, sync/async, a multi-key tie, five other comparisons; case, accents,
        trailing space, "a"/"a\t", ß/ss, empty string, U+1F600 vs U+FF5E/U+E000, null). Wrong before: InMemory, JSON, XML,
        RavenDB, MongoDB, Elasticsearch, Cosmos 40 of 56 (culture order: `apple` before `Banana`); PostgreSQL, MySQL,
        TimescaleDB 56 (collation; PostgreSQL NULL last); SQL Server 46 (padding put "a\t" before "a"); SQLite 28 (BINARY
        put U+1F600 after U+FF5E); no SQL provider refused another comparison. SQL views 50 of 64.
      → MongoDB with a collection default collation (`en`, strengths 1–3): `$min`/`$max` followed the locale, `$group`
        merged `zebra`/`Zebra` (strengths 1–2) and `$match` matched `Zebra` for `== "zebra"`. Decision recorded above.
- [x] Implement or refuse by name; tests red before.
      → `AggregateHelper.ApplyOrderingAndPaging` orders with `AggregateMath.ValueComparer` in `query.StringComparison`
        (LinqAggregate, MongoDB, Elasticsearch, Cosmos callers). SQL: an ORDER BY on a string group column or string
        MIN/MAX renders through `SqlValueFragmentDialect.OrdinalOrderTerms`; one per-dialect key `OrdinalOrderKey` (TASK-907's
        `OrdinalStringExtreme` now derives from it, SQL unchanged; PostgreSQL adds a NULL-first term); the key wraps the
        aggregate expression, not its alias; any other comparison refused by name (`StringRowOrderNotSupported`) before a
        request. Aggregating SQL views ordered by a string group column / MIN / MAX use the same terms, on the fly and
        persistent. MongoDB aggregates run with `Collation.Simple` (`AggregationPipelineBuilder.WithCollation`), so
        `$min`/`$max`, `$group` and `$match` answer as C# on a collated collection. Ties: documented (stable in memory, no
        defined order on SQL; add a unique key — the contract checks multi-key paging).
      → Tests: the contract (56/56 on 12 providers), `Task909ViewRowOrderTests` (64), `Task909MongoCollationLiveTests`
        (6 red → green).
      → Live: 119 of 120 importer suites of the changed shared projects green on the working tree (the one failure is a Redis test in EventBus.MessageQueue: no Redis host); staged HEAD + patch snapshot: Data 335, InMemory 445, SQL.Tests 1125, SQL.Views 4027, SQLite 2466, PostgreSQL 1886, MySQL 1908, SQL Server 1942, MongoDB 521, Elasticsearch 724, Cosmos aggregate tests 124 - all green.
      → No consumer uses aggregates or aggregating views.
      → **Found, filed:** TASK-911 (P1: SQL Server and MySQL GROUP BY on strings merges keys by collation), TASK-912
        (MongoDB reads/views on a collated collection; PostgreSQL NULL position for non-string keys; NoSQL view row
        order; Guid/enum key order).

## Out of scope

- String MIN/MAX order (TASK-907, done).

## Human test plan

N/A — fully covered by automated tests.

## Implementation plan
