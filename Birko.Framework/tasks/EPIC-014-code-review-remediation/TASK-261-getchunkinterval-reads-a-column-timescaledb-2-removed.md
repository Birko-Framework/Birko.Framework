---
id: TASK-261
parent: EPIC-014
feature: FEATURE-014
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: done
priority: P2
assignee: ai
created: 2026-08-18
depends-on: []
blocks: []
related: [TASK-253, TASK-472]
findings: []
pr: "Birko.Data.Migrations.TimescaleDB b9566d9 · tests: Birko.Data.Migrations.TimescaleDB.Tests 447fe35"
github-issue: null
jira-key: null
affects: [Birko.Data.Migrations.TimescaleDB]
---

# `GetChunkInterval` reads a catalogue column TimescaleDB removed in 2.0

Found while writing [[TASK-253]]'s live suite against **TimescaleDB 2.29.2 / PostgreSQL 16** — and found the
way these things usually are: my own first draft of a chunk-interval assertion failed for exactly the same
reason the product code does.

## What is wrong

`Birko.Data.Migrations.TimescaleDB/TimescaleDBMigration.cs:302`:

```csharp
command.CommandText = "SELECT chunk_time_interval::text FROM timescaledb_information.hypertables WHERE hypertable_name = @table";
```

`timescaledb_information.hypertables` has **no `chunk_time_interval` column**. TimescaleDB 2.0 moved the chunk
interval to `timescaledb_information.dimensions` and renamed it **`time_interval`** (with `integer_interval` as
the sibling for integer-partitioned hypertables). Measured on 2.29.2:

```
ERROR:  column "chunk_time_interval" does not exist
```

The view's current columns are `hypertable_schema`, `hypertable_name`, `owner`, `num_dimensions`, `num_chunks`,
`compression_enabled`, `tablespaces`, `primary_dimension`, `primary_dimension_type` — that is all.

So the method raises `42703` on **every TimescaleDB 2.x server**, which is every supported version. It is not
swallowed: `ExecuteScalar` runs on the migration's own connection with no `try`/`catch` above it, so a migration
calling it fails outright.

**This is not TASK-253's defect.** That task is about identifier quoting and folding; this is catalogue schema
drift — a different cause with a different fix. It is filed separately for that reason, not because it is small.

## Blast radius: latent

Swept while filing: the only references to `GetChunkInterval` in the whole tree are its own declaration and
TASK-253's new test. No consumer calls it, and `Birko.Data.Migrations.TimescaleDB` has exactly **one** importer
in the family (`Consumers/Birko.Sandbox/Birko.Framework/Birko.Framework.csproj`). So nothing is failing in
production today — this is broken public surface, not an incident. Say so in the commit; TASK-246 had to be
corrected after the fact for overstating live impact.

`IsHypertable`, immediately above it, is **fine** — `hypertable_name` still exists, and TASK-253 added a live
test pinning that it finds a PascalCase hypertable and that a folded name finds nothing.

## Already pinned

`Birko.Data.Migrations.TimescaleDB.Tests/MigrationEmitterLiveTests.cs` carries
`GetChunkInterval_readsAColumnTimescaleDB2Removed_TASK261`, asserting the `42703` as **current** behaviour. So
this task starts with a failing-in-the-right-direction test: invert it rather than write it.

## Acceptance criteria

- [x] Reads `time_interval` from `timescaledb_information.dimensions`, restricted to `dimension_number = 1`.
      ⚠ **But the stated rationale was wrong, and the honest version is recorded instead.** The view carries
      its **own `ORDER BY`** (measured), so dimension 1 comes back first and an unrestricted query takes the
      *right* row on 2.29.2 — removing the clause fails **no test**. The clause is kept as **defensive**, not
      as a fix for an observed failure: 2 rows come back and only 1 carries a value, so correctness without it
      rests on an ordering the query does not state and the catalogue does not promise — which is the same bet
      that produced the defect this task fixes.
- [x] The integer-partitioned case answered explicitly. → **Coalesce**:
      `COALESCE(time_interval::text, integer_interval::text)`. Returning null would claim no interval is
      configured when one is — measured, an integer-partitioned hypertable has `time_interval` NULL and
      `integer_interval = 100000`. **The discriminator is which column is populated, NOT `dimension_type`**:
      measured on 2.29.2, an integer-partitioned dimension still reports `dimension_type = 'Time'`, so
      branching on the type would have been wrong. Accepted cost, written on the method: a caller cannot tell
      `"3 days"` from `"100000"` without knowing the partitioning column's type — but a `string?` return can
      express neither shape better, and losing the value is worse.
- [x] The pin inverted. → `GetChunkInterval_readsAColumnTimescaleDB2Removed_TASK261` is **gone**, replaced by
      `GetChunkInterval_returnsThePrimaryDimensionsInterval`. The `42703` assertion is removed rather than kept
      beside the new one, since two tests asserting opposite things about one method is a contradiction for the
      next reader to resolve, not extra coverage.
- [x] Verified against live TimescaleDB 2.29.2 / PostgreSQL 16.15. → Five tests, all on returned values:
      `"3 days"` round-trips; the space-partitioned hypertable yields its **time** dimension's `"7 days"`; an
      integer-partitioned one yields `"100000"`; a table that was never converted yields **null** rather than
      throwing; and a separate test pins the catalogue shape that justifies the `dimension_number` clause
      (2 dimension rows, 1 with an interval) — asserted against the server, because the reader itself cannot
      witness it.
- [x] Proven able to fail — three reverts, one per clause, and **one of them fails nothing**, which is
      reported rather than hidden:
      - **(a)** restore `chunk_time_interval` from `…hypertables` → **4 of 56** fail (every `GetChunkInterval`
        test).
      - **(b)** drop only `AND dimension_number = 1` → **0 of 56**. The view's own `ORDER BY` makes the
        unrestricted query take the right row today. Recorded on the method and in the space-partitioned
        test's remarks, so the clause does not read as witnessed when it is not, and the hazard is pinned by a
        catalogue assertion instead.
      - **(c)** drop only the `COALESCE` → **1 of 56**, exactly the integer-partitioned case.
- [x] A version note added. → The remark now states it targets **TimescaleDB 2.x**, names the measured server
      (2.29.2 / PostgreSQL 16.15), and records that the previous spelling was presumably right on 1.x with
      nothing marking its expiry — which is why the version is now written down.

## Verification

Live **TimescaleDB 2.29.2 / PostgreSQL 16.15**, plus SQL Server 2022, PostgreSQL 16, MySQL 8.4 and on-disk
SQLite, with `BIRKO_REQUIRE_LIVE` set throughout. **1,229 passed, 0 failed** across nine suites; **4 net new**
(five added, the inverted pin replacing one).

| Suite | Result |
|---|---|
| `Birko.Data.Migrations.TimescaleDB.Tests` | 56 (was 52) |
| `Birko.Data.TimescaleDB.Tests` | 44 |
| `Birko.Data.SQL.Tests` | 587 |
| `Birko.Data.SQL.MSSql.Tests` | 87 |
| `Birko.Data.SQL.MySQL.Tests` | 78 |
| `Birko.Data.SQL.PostgreSQL.Tests` | 82 |
| `Birko.Data.SQL.SqLite.Tests` | 229 |
| `Birko.Data.Migrations.SQL.Tests` | 49 |
| `Birko.Data.Migrations.Tests` | 17 |

### The out-of-scope survey was run, and it is clean

`## Out of scope` asked for other stale `timescaledb_information` queries to be spawned if any turned up.
Swept: the framework contains exactly **two** catalogue queries — `IsHypertable`'s
`timescaledb_information.hypertables WHERE hypertable_name` (valid; the column exists on 2.29.2 and has live
coverage) and this one. The remaining `chunk_time_interval` occurrences are the **named argument** to
`create_hypertable`, which is current and unrelated to the removed view column. No `_timescaledb_catalog`
(internal) use anywhere. **Nothing to spawn.**

## Out of scope

- Identifier quoting and folding in this file — **[[TASK-253]] owns those** and has landed.
- `IsHypertable`, which is correct and now has live coverage.
- The hardcoded `time` bucketing column (**[[TASK-255]]**) and the raw SQL fragments (**[[TASK-260]]**).
- Auditing the rest of the framework for other stale `timescaledb_information` queries. Worth doing, but it is a
  survey rather than this fix; if this task's sweep turns any up, spawn them rather than absorbing them.

## Human test plan

- [x] N/A — mechanical; the proof is a known chunk interval read back from a live hypertable, including the
      space-partitioned and integer-partitioned shapes. All three are asserted on returned values.
