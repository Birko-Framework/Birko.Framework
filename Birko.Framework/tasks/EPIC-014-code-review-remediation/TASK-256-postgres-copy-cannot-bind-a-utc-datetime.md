---
id: TASK-256
parent: EPIC-014
feature: FEATURE-014
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: todo
priority: P1
assignee: ai
created: 2026-08-18
depends-on: []
blocks: []
related: [TASK-242, TASK-243]
findings: []
pr: null
github-issue: null
jira-key: null
---

# PostgreSQL's binary `COPY` cannot bind a UTC `DateTime`, and the test suite is green because its fixture avoids it

## Context — found by consumer Symbio (TASK-472) running a real entity through `BulkInsertAsync`

Symbio stood up live servers to verify the boundary from the consumer side. The first bulk insert against
PostgreSQL 16.15 failed, and not on anything to do with the boundary:

```
System.Exception : COPY "TxProviderProbes" (Label, TenantGuid, …, CreatedAt, UpdatedAt, PrevUpdatedAt)
                   FROM STDIN (FORMAT BINARY)
---- System.ArgumentException : Cannot write DateTime with Kind=UTC to PostgreSQL type
     'timestamp without time zone', consider using 'timestamp with time zone'.
   at PostgreSQLConnector.BulkInsertAsync (PostgreSQLConnector.cs:479)
```

Two decisions in the same connector contradict each other:

- `ConvertType` maps `DbType.Date`/`DateTime`/`DateTime2` → **`TIMESTAMP`** (no time zone) — `:203-205`.
- `DbTypeToNpgsqlDbType` maps the same → **`NpgsqlDbType.Timestamp`** — `:349`. The binary COPY writer passes
  that explicitly, so Npgsql cannot infer, and Npgsql 6+ **refuses** a `Kind=Utc` value for a
  timezone-less column.

⚠ **The important part: this is already known, and worked around in a fixture rather than fixed.**
`Birko.Data.TimescaleDB.Tests/BulkTransactionBoundaryLiveTests.cs:177-181` builds its rows with
`DateTimeKind.Unspecified` and documents exactly why — *"the mapped column is `timestamp without time zone`
and Npgsql refuses to write a UTC-kinded DateTime to it."* So the live suites added by TASK-242/243 are green
**because the probe entity avoids the shape a real consumer has**.

Symbio cannot avoid it: `BaseEntity`/`AuditableEntity` set `CreatedAt`/`UpdatedAt` from `DateTime.UtcNow`, so
every Symbio entity is UTC-kinded. `CreateManyAsync` therefore throws on PostgreSQL **and TimescaleDB** (which
inherits the type map) for every entity in the product.

⚠ **Scope: the binary COPY path only.** A single-row insert goes through a parameterised command where Npgsql
infers from the value, so `CreateAsync` works. Measured in the same run: `UpdateManyAsync`, `DeleteManyAsync`
and `DeleteWhereAsync` all succeeded against the same server — they are statement-based.

## Why it matters

`CreateManyAsync` is one of the five collection members. A consumer on PostgreSQL — the provider most likely
in production — fails on every operation that creates rows in bulk. It went unnoticed because every consumer
test runs on SQLite and the framework's own Postgres/Timescale fixtures use `Unspecified`.

## What to decide

1. **Map `DateTime` → `TIMESTAMPTZ`** (`NpgsqlDbType.TimestampTz`). Correct for UTC values, but changes the
   column type of every existing deployment, and reading an existing `TIMESTAMP` as `TIMESTAMPTZ`
   reinterprets it in the server's timezone.
2. **Normalise at the COPY boundary** — `DateTime.SpecifyKind(v, Unspecified)` when the target is
   `Timestamp`. No schema change; silently discards `Kind`, so UTC and local become indistinguishable.
3. **Normalise only in the binary writer**, leaving the parameterised path alone. Smallest diff; keeps the two
   paths meaning different things, which is how this arose.

Whichever is chosen, state what a Birko `DateTime` column *means* on PostgreSQL — the absence of that rule is
the root cause.

## Acceptance criteria

- [ ] A decision recorded with its reason, stating what a `DateTime` column means on PostgreSQL.
- [ ] `BulkInsertAsync` succeeds for a **UTC-kinded** entity against a real PostgreSQL, with the round-tripped
      value asserted — not merely the absence of an exception.
- [ ] The Postgres and TimescaleDB live fixtures use a **UTC-kinded** probe, so the suite stops passing by
      avoiding the shape consumers actually have. This is the criterion that closes the real gap.
- [ ] TimescaleDB verified too — it inherits both the type map and the fix.
- [ ] Stated explicitly what happens to a database that already holds `TIMESTAMP` values.
- [ ] Proven able to fail.

## Out of scope

- The transaction boundary — TASK-242/243. This is value binding, not which connection a write runs on.
- MSSql's `TEXT` mapping — TASK-257, the sibling found in the same consumer run.

## Human test plan

- [ ] N/A — mechanical; the proof is a UTC-kinded bulk insert against a real PostgreSQL and the value read back.
