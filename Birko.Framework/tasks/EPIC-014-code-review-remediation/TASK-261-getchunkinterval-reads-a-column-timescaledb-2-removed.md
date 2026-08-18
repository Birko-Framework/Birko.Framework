---
id: TASK-261
parent: EPIC-014
feature: FEATURE-014
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: todo
priority: P2
assignee: ai
created: 2026-08-18
depends-on: []
blocks: []
related: [TASK-253, TASK-472]
findings: []
pr: null
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

- [ ] `GetChunkInterval` reads `time_interval` from `timescaledb_information.dimensions`, restricted to the
      **primary time dimension** (`dimension_number = 1`) — the view has one row per dimension, so an
      unrestricted query returns two rows for a space-partitioned hypertable and `ExecuteScalar` would silently
      take whichever came first.
- [ ] The **integer-partitioned** case answered explicitly, not by accident: `time_interval` is NULL for a
      hypertable partitioned on an integer column, where the value lives in `integer_interval`. Decide whether
      to return the integer interval, return null, or coalesce — and write down which, because a silent null
      here reads as "no chunk interval configured".
- [ ] The existing TASK-261 pin inverted: it must assert the interval is returned, and the `42703` assertion
      must be gone rather than left alongside as a contradiction.
- [ ] Verified against **live TimescaleDB**, asserting a known interval round-trips (`"3 days"` in), including
      the space-partitioned case. Not "the call did not throw".
- [ ] Proven able to fail: restore the old column name and watch the new test go red.
- [ ] A version note in the doc comment saying which TimescaleDB versions the query targets. The old query was
      presumably correct on 1.x, and nothing recorded that it had an expiry.

## Out of scope

- Identifier quoting and folding in this file — **[[TASK-253]] owns those** and has landed.
- `IsHypertable`, which is correct and now has live coverage.
- The hardcoded `time` bucketing column (**[[TASK-255]]**) and the raw SQL fragments (**[[TASK-260]]**).
- Auditing the rest of the framework for other stale `timescaledb_information` queries. Worth doing, but it is a
  survey rather than this fix; if this task's sweep turns any up, spawn them rather than absorbing them.

## Human test plan

- [ ] N/A — mechanical; the proof is a known chunk interval read back from a live hypertable, including the
      space-partitioned and integer-partitioned shapes.
