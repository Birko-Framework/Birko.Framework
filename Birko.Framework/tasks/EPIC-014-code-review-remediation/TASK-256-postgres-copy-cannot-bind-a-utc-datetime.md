---
id: TASK-256
parent: EPIC-014
feature: FEATURE-014
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: done
priority: P1
assignee: ai
created: 2026-08-18
depends-on: []
blocks: [TASK-263]
related: [TASK-242, TASK-243]
findings: []
pr: 4314c42
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

## Decision — what a Birko `DateTime` column means on PostgreSQL

**Option 2, applied at every write boundary.** Recorded 2026-08-19, verified against live PostgreSQL 16 and
TimescaleDB 2 / PostgreSQL 16 with Npgsql 10.0.3.

> **A Birko `DateTime` column on PostgreSQL is `timestamp without time zone` and stores the wall-clock
> components of the value as supplied. `DateTimeKind` is not persisted; every read returns
> `Kind=Unspecified`. A write boundary that binds an **un-prepared** value strips `Kind` first, so the
> stored value is independent of the server's `TimeZone` setting and identical on the single-row and bulk
> paths. A value whose offset must survive needs the opt-in timezone-aware column type — [[TASK-263]],
> which does not exist yet.**

**"Un-prepared" is doing real work in that sentence, and it was found at the close gate rather than
assumed.** Two sinks bind un-prepared and are normalised: `AddParameter` and the binary `COPY` writer. The
bulk **update** and **delete** paths bypass `AddParameter` entirely — they pre-create parameters holding
`DBNull.Value`, call `command.Prepare()`, then assign `.Value` per row — and are correct by a *different*
mechanism: `Prepare()` pins each parameter to the target column's real type before any value is assigned, so
the value is never re-inferred as `timestamptz`. Measured on a non-UTC server: unshifted, and the
delete-shaped filter matches the stored row. Pinned by
`Bulk_update_does_not_shift_a_utc_value_on_a_non_utc_server`, because that `Prepare()` is the only thing
holding six binding sites correct and nothing else would notice its removal. The first draft of this rule
said "every write boundary strips `Kind`" — a claim my own measurement falsified, and exactly the shape
[[TASK-258]] is about.

### The measurement the decision rests on

`DateTime(2026-03-15 10:30:00)` into a `TIMESTAMP` column. `AddParameter` binds **no** `DbType`, so Npgsql
infers from the value's `Kind`; the binary COPY writer passes `NpgsqlDbType.Timestamp` explicitly.

| `Kind` | parameterised (`CreateAsync`) | binary COPY (`CreateManyAsync`) |
|---|---|---|
| **`Utc`** | inferred **`TimestampTz`** → cast into the column **through the session `TimeZone`**: `10:30` @UTC, **`11:30`** @`Europe/Bratislava` — **silently shifted** | **threw** `ArgumentException` |
| `Local` | `10:30`, TZ-independent | `10:30` |
| `Unspecified` | `10:30`, TZ-independent | `10:30` |

Read-back is **always `Kind=Unspecified`**, including for the row stored correctly — so `Kind` never
round-tripped on either path, before or after this fix.

Two conclusions the filed task did not have:

- **`Kind=Utc` was broken on BOTH paths**, loudly on one and silently on the other. The task scoped itself to
  the binary COPY path and stated that `CreateAsync` "works"; it works only on a server whose `TimeZone` is
  UTC. Fixing COPY alone would have left the two paths storing **different instants**, and a bulk-written row
  would not have matched a filter bound through the parameterised path.
- **The UTC-kinded value is the framework's own default, not a consumer choice.**
  `Birko.Data.SQL/Models/AbstractLogModel.cs:17-18` initialises `CreatedAt`/`UpdatedAt` from
  `DateTime.UtcNow`, and `AsyncTimestampStoreWrapper` re-stamps `UpdatedAt` from `_clock.UtcNow`. The
  framework's canonical SQL base model emitted exactly the value its own PostgreSQL connector could not
  bulk-insert — for every `AbstractDatabaseLogModel` descendant, on every consumer.

### Why not the alternatives

- **Option 1 — `TIMESTAMPTZ`.** It does round-trip `Kind=Utc` correctly, and it was reopened during planning
  because it is at its cheapest *right now* (no PostgreSQL data exists, models are about to freeze). Rejected
  on three measured grounds: it makes PostgreSQL the **only** tz-aware provider — SQLite (numeric), MySQL
  (`DATETIME`) and MSSql (`DATETIME2`) all store a timezone-less wall clock — so a SQLite-green test would
  stop proving production behaviour for a product that tests on SQLite and deploys on PostgreSQL; it **breaks
  the `Unspecified` cell** (`10:30` in reads back `09:30Z`); and its migration silently shifts data
  (`ALTER COLUMN ts TYPE TIMESTAMPTZ` reinterprets stored values in the session TZ *at ALTER time*, `10:30` →
  `09:30Z`, unless run under `SET TimeZone TO 'UTC'`). It also earns nothing for a consumer whose converter
  re-attaches UTC anyway — see below.
- **Option 3 — COPY only.** Rejected: see the first conclusion above. Not the smallest diff, the worst outcome.
- **Option 4 — fail loudly on both paths** (bind an explicit type so the parameterised path throws too), per
  § SH-H037's fail-fast doctrine. Rejected on blast radius: `AbstractLogModel` emits `Kind=Utc`, so this would
  refuse a write from *every* framework entity. § SH-H037 requires the radius to be cleared first, and here it
  says no — the same inversion as TASK-248.

### This is already the consumer-side contract

Symbio's `UtcDateTimeJsonConverter` treats an `Unspecified` value from storage **as UTC**
(`_ => SpecifyKind(dt, Utc)`) and always emits `Z`, documenting that "server-generated `DateTime.UtcNow`
values are stored and compared consistently as UTC throughout the system". Display conversion runs through
`Birko.Time`'s `ITimeZoneConverter` with `Time:DefaultTimeZone` — an **explicit** zone conversion from a UTC
baseline, not `.ToLocalTime()` on an ambiguous `Kind`. So the "caller re-attaches the Kind" half of the rule
was already written and shipped; option 2 writes down what the consumer already assumes and needs no consumer
change.

### What happens to a database that already holds `TIMESTAMP` values

**No schema change, so stored rows are untouched.** The only behaviour change is that a `Kind=Utc` value
written through the **single-row** path on a **non-UTC** server now stores the UTC wall clock instead of a
session-shifted one.

That is not behaviour-preserving for such a server, and the honest statement is: today the write is shifted
*and the filter binding is shifted identically*, so writes and reads are consistently wrong and agree with
each other. Unshifting the filter while stored rows stay shifted makes previously-findable rows invisible.
Rows already written that way **stay** shifted; this fix does not migrate them, and **no blanket `UPDATE`
safely could**, because rows written before and after the fix are indistinguishable in the column.

**This is acceptable only because the blast radius measured zero.** `DataProviders.Default` in
`Symbio.Api/appsettings.json:9` is `SQLite`; its `PostgreSql` entry is a template with empty credentials, and
Development overrides to SQLite. Across all 16 consumer repos only Symbio and Birko.Sandbox mention
PostgreSQL, and Sandbox has none in source. **No deployment holds PostgreSQL data.** The window is expiring by
plan — production is expected to move to PostgreSQL once models freeze — which is why both paths were fixed
now rather than the loud one only. On a server holding real non-UTC data this fix would need a migration first.

### Proven able to fail

| Revert | Result |
|---|---|
| Binary COPY writer un-wired (sync + async) | **16 of 76** fail (PostgreSQL) |
| `AddParameter` un-wired only — the silent half | **1 of 76**, and it is exactly `Parameterised_and_bulk_agree_on_a_non_utc_server` |
| Base COPY writer un-wired, TimescaleDB suite | **15 of 44** fail — inheritance measured, not read |

The second row is the important one: on a UTC server that revert fails **nothing**, because both paths store
`10:30` either way. The dedicated non-UTC database is the only thing that can tell that half of the fix from a
no-op. `PostgreSqlSettings.GetConnectionString()` emits no `Timezone` key and offers no raw escape hatch, so
`SET TimeZone` on a test's own connection cannot reach the store's — and `NpgsqlConnection.ClearAllPools()`
after the `ALTER DATABASE` is mandatory, since a pooled connection otherwise keeps `Etc/UTC` and the test
silently measures nothing.

## Acceptance criteria

- [x] A decision recorded with its reason, stating what a `DateTime` column means on PostgreSQL. — `## Decision` above.
- [x] `BulkInsertAsync` succeeds for a **UTC-kinded** entity against a real PostgreSQL, with the round-tripped
      value asserted — not merely the absence of an exception. — `UtcDateTimeBindingLiveTests`: the stored
      text is asserted via `::text` on a connection of its own, and the store round trip asserts the value
      **and** that `Kind` comes back `Unspecified`.
- [x] The Postgres and TimescaleDB live fixtures use a **UTC-kinded** probe, so the suite stops passing by
      avoiding the shape consumers actually have. This is the criterion that closes the real gap. — TimescaleDB's
      `Rows()` flipped `Unspecified` → `Utc` and the workaround comment deleted; PostgreSQL's `BulkRow` had **no
      `DateTime` property at all**, so a UTC-kinded `Ts` was added to the model and its `ModelMap`. This is what
      makes the COPY revert fail **16** rather than a handful.
- [x] TimescaleDB verified too — it inherits both the type map and the fix. — 44 green against live
      TimescaleDB 2 / PG16 (2 new), including a stored-value assertion **over a real hypertable**; the
      inheritance is proven by revert (15 of 44), not by reading the class hierarchy.
- [x] Stated explicitly what happens to a database that already holds `TIMESTAMP` values. — `## Decision`
      § *What happens to a database that already holds `TIMESTAMP` values*, including that no blanket `UPDATE`
      can safely repair a non-UTC server, and that this is acceptable only because the radius measured zero.
- [x] Proven able to fail. — three independent reverts, table in `## Decision`. The `AddParameter` revert
      fails exactly 1 of 76, which is the whole reason the non-UTC database exists.

## Out of scope

- The transaction boundary — TASK-242/243. This is value binding, not which connection a write runs on.
- MSSql's `TEXT` mapping — TASK-257, the sibling found in the same consumer run.
- Deferred to TASK-263 — the opt-in timezone-aware column type this task's rule names as the escape
  hatch for a value whose offset must survive. Mapped in every connector, unreachable from a model.

## Implementation plan

⚠ **Acceptance criteria question:** criterion 2 says "`BulkInsertAsync` succeeds for a UTC-kinded entity".
The measurement below shows the *parameterised* path is also wrong for a UTC-kinded value — silently, and
live in production today — so a fix scoped to `BulkInsertAsync` alone would leave `CreateAsync` and
`CreateManyAsync` storing **different instants** for the same value. I read criterion 1 ("state what a
`DateTime` column *means* on PostgreSQL") as already covering both paths, and plan accordingly. Flagging
rather than editing the criteria.

### Measured baseline — live PostgreSQL 16 (`Etc/UTC` server default), Npgsql 10.0.3

Probe: `DateTime(2026-03-15 10:30:00)` into a `timestamp without time zone` column.
`AbstractConnectorBase.AddParameter` (`:338-352`) and `PostgreSQLConnector.AddParameter` (`:304-321`) set
**no** `DbType` — value only — so Npgsql infers from the CLR value's `Kind`.

| `Kind` | parameterised (`CreateAsync`) | binary COPY (`CreateManyAsync`) |
|---|---|---|
| **`Utc`** | inferred **`TimestampTz`** → server casts to `timestamp` **using session `TimeZone`**: `10:30` @UTC, **`11:30`** @`Europe/Bratislava` — **silently shifted** | **throws** `ArgumentException` |
| `Local` | inferred `Timestamp` → `10:30`, TZ-independent | `10:30` |
| `Unspecified` | inferred `Timestamp` → `10:30`, TZ-independent | `10:30` |

Read-back from a `TIMESTAMP` column is **always `Kind=Unspecified`** — every row above, including the one
stored correctly. So `Kind` never round-trips on PostgreSQL today, on either path.

**`Kind=Utc` is the only broken cell, and it is broken in BOTH paths — loudly in COPY, silently in the
parameterised one.** The task described only the loud half. The silent half is the dangerous one
(§ Conventions, *the loud provider is not the dangerous one*).

**And the UTC-kinded value is the FRAMEWORK's own default, not a consumer choice.**
`Birko.Data.SQL/Models/AbstractLogModel.cs:17-18` initialises `CreatedAt`/`UpdatedAt` to `DateTime.UtcNow`,
and `AsyncTimestampStoreWrapper` re-stamps `UpdatedAt` from `_clock.UtcNow`. So the framework's canonical SQL
base model emits precisely the value its own PostgreSQL connector cannot bulk-insert — every
`AbstractDatabaseLogModel` descendant on every consumer, not just Symbio's entities.

### Blast radius — measured, zero today, and expiring by plan

`Symbio/src/Host/Symbio.Api/appsettings.json:9` sets `DataProviders.Default` to **`SQLite`**; the
`PostgreSql` entry is a template with empty credentials, and `appsettings.Development.json` overrides to
SQLite too. Across all 16 consumer repos only Symbio and Birko.Sandbox reference PostgreSQL at all, and
Sandbox has no PostgreSQL in source. **No deployment holds PostgreSQL data**, so fixing the parameterised
path cannot orphan stored rows.

This matters because the fix is **not** behaviour-preserving on a non-UTC server that already holds data:
today a `Kind=Utc` value is shifted on write *and* shifted identically when bound into a `WHERE` clause, so
writes and filters are consistently wrong and agree with each other. Unshifting the filter while stored rows
stay shifted would make existing rows invisible to existing queries. That cost is zero only while no data
exists — and the window closes when models freeze and production moves to PostgreSQL (confirmed as the plan).
Same shape as TASK-219: *the migration objection was measured away, and it was expiring.*

### Options rejected, with the measurement that rejected each

- **Option 1 — map `DateTime` → `TIMESTAMPTZ`.** `Kind=Utc` does round-trip correctly (back as `Kind=Utc`,
  equal). But it makes PostgreSQL the **only** tz-aware provider — SQLite (numeric), MySQL (`DATETIME`) and
  MSSql (`DATETIME2`) all store a timezone-less wall clock — and since testing is on SQLite while production
  moves to PostgreSQL, a SQLite-green test would stop proving production behaviour. It also breaks the
  `Unspecified` cell: measured, `10:30` in reads back `09:30Z`, reinterpreted from the session TZ. And the
  migration is a trap — `ALTER COLUMN ts TYPE TIMESTAMPTZ` reinterprets stored values in the session TZ *at
  ALTER time* (`10:30` → `10:30+01` = `09:30Z`), so it silently shifts unless run under
  `SET TimeZone TO 'UTC'`.
- **Option 3 — COPY only.** Not the smallest diff but the worst outcome: the two write paths would store
  different instants on any non-UTC server, and a row written by the fixed COPY would **not match** a filter
  bound through the unfixed parameter path. Two producers for one value.
- **Option 4 — fail loudly on both paths** (bind an explicit `Timestamp` type so the parameterised path
  throws like COPY does), per § SH-H037's fail-fast doctrine. Rejected on blast radius: `AbstractLogModel`
  emits `Kind=Utc`, so this would refuse a write from *every* framework entity. § SH-H037 requires the radius
  to be cleared first, and here it says no — same inversion as TASK-248.

### Decision — option 2, applied at every write boundary

> **A Birko `DateTime` column on PostgreSQL is `timestamp without time zone` and stores the wall-clock
> components of the value as supplied. `DateTimeKind` is not persisted; every read returns
> `Kind=Unspecified`. Every write boundary strips `Kind` before binding, so the stored value is
> independent of the server's `TimeZone` setting and identical on the single-row and bulk paths. A value
> whose offset must survive needs the opt-in timezone-aware column type — [[TASK-263]].**

**This is already the consumer-side contract.** Symbio's `UtcDateTimeJsonConverter` treats an
`Unspecified` value from storage as UTC (`_ => SpecifyKind(dt, Utc)`) and always emits `Z`, documenting that
"server-generated `DateTime.UtcNow` values are stored and compared consistently as UTC throughout the
system"; display conversion goes through `Birko.Time`'s `ITimeZoneConverter` with `Time:DefaultTimeZone`, an
explicit zone conversion from a UTC baseline rather than `.ToLocalTime()` on an ambiguous `Kind`. So the
"caller re-attaches the Kind" half of the rule is already written and shipped — option 2 writes down what the
consumer already assumes, and needs no consumer change.

The fix is also **narrow by construction**: it changes only the `Utc` cell, because
`SpecifyKind(…, Unspecified)` is already a no-op in effect for the `Local` and `Unspecified` cells (measured).

### Steps

1. **Record the decision** — a `## Decision` section in this file carrying the matrix and the rule verbatim
   (acceptance 1), plus the existing-data statement from step 8 (acceptance 5).

2. **One producer for the normalisation.** Add a single private static helper on `PostgreSQLConnector` —
   `NormalizeTimestampValue(object? value)` — returning `DateTime.SpecifyKind(d, Unspecified)` for a
   `DateTime`, everything else untouched. Both sinks call it; neither re-derives it (§ Conventions,
   *one producer*). **Not** on `AbstractConnectorBase`: the other three providers are unmeasured, and wiring
   a guard blind is how a refusal fires on a case it was never about.
   Document on it **why binding every `DateTime` parameter is safe today** — the premise is stronger than a
   `DateTimeOffset` argument alone: `DateTimeField` hardcodes `DbType.DateTime` (`:11-14`), **no attribute in
   `Attributes/Field.cs` can override a field's `DbType`**, and no field class produces `DbType.Date`,
   `DbType.Time` or `DbType.DateTimeOffset` at all. So every `DateTime` property maps to `TIMESTAMP`, by the
   only path there is. That premise expires the moment [[TASK-263]] lands a tz-aware arm, so it is written
   down rather than assumed, and [[TASK-263]] must revisit this helper.

3. **Wire the COPY writer** — sync `:425` and async `:478`, both `writer.Write(value, DbTypeToNpgsqlDbType(...))`.

4. **Wire the parameterised path** — `PostgreSQLConnector.AddParameter` (`:308`), beside the existing
   `NormalizeParameterValue` call, covering **both** the new-parameter and the `Contains(name)` reuse branch.
   `NormalizeParameterValue` is `static`, not virtual, so it is not the seam — do not try to extend it.

5. **TimescaleDB inherits both** — verified: `Birko.Data.TimescaleDB` overrides neither `AddParameter` nor
   `BulkInsert*`, and `DbTypeToNpgsqlDbType` is `private static`. Confirm by revert, not by reading
   (§ Conventions, *a funnel with four overrides is not a funnel*), and give it its own live test
   (acceptance 4).

6. **De-rig the fixtures** (acceptance 3 — "the criterion that closes the real gap"):
   - `Birko.Data.TimescaleDB.Tests/BulkTransactionBoundaryLiveTests.cs:177-181` — `Ts` becomes
     `DateTimeKind.Utc`; delete the comment documenting the avoidance.
   - `Birko.Data.SQL.PostgreSQL.Tests/BulkTransactionBoundaryLiveTests.cs:80-94` — `BulkRow` has **no
     `DateTime` property at all**, so that suite avoids the shape by omission, more completely than
     TimescaleDB's `Unspecified`. Add a UTC-kinded `Ts` to the model and its `ModelMap`.

7. **Tests** (`Birko.Data.SQL.PostgreSQL.Tests`, `Birko.Data.TimescaleDB.Tests`):
   - UTC-kinded `CreateManyAsync` against live PostgreSQL, **value read back and asserted** — not absence of
     an exception (acceptance 2).
   - **The non-UTC test, which pins the silent half — and it is mandatory, not optional.** On a UTC server the
     parameterised fix is *unobservable* (both stored values are `10:30`), so without this test reverting step
     4 fails **nothing** — a revert that fails nothing is a missing test (§ Conventions, TASK-248).
     Mechanism, measured: `PostgreSqlSettings.GetConnectionString()` emits no `Timezone` key and offers no raw
     escape hatch, so `SET TimeZone` on the test's own connection **cannot** reach the store's connection.
     Instead create a **dedicated throwaway database** (`CREATE DATABASE … ; ALTER DATABASE … SET TimeZone TO
     'Europe/Bratislava'`), point the probe store's `Settings.Name` at it, and drop it in teardown — isolated,
     so no concurrently-running live suite is affected and a crashed run leaves nothing behind.
     **`NpgsqlConnection.ClearAllPools()` is mandatory** after the `ALTER`: measured, a pooled connection keeps
     `Etc/UTC` and the test silently measures nothing. Assert single-row and bulk writes of the same UTC-kinded
     value store the **same** value.
   - **Filter round-trip:** bulk-insert UTC-kinded, then read back with a filter bound from a UTC-kinded
     `DateTime`, and assert the row is found — pins that write and filter agree about the value.
   - `Kind` matrix pinned: `Utc`/`Local`/`Unspecified` all store the same wall clock.
   - Read-back `Kind` is `Unspecified` — pins the documented contract, so a later "improvement" to
     `TIMESTAMPTZ` cannot land silently.
   - Confirm the other three providers stay green (`ConvertType` untouched, no schema change).

8. **Existing data** (acceptance 5): **no schema change, so stored `TIMESTAMP` rows are untouched.** The only
   behaviour change is that a `Kind=Utc` value written through the single-row path on a **non-UTC** server now
   stores the UTC wall clock instead of a session-shifted one. Rows already written that way on such a server
   are shifted and **stay** shifted — this fix does not migrate them, and no blanket `UPDATE` can safely do so
   either, because rows written before and after the fix are indistinguishable in the column. That is
   acceptable only because the blast radius measured **zero** (see above); on a server holding real non-UTC
   data this fix would need a migration first.

9. **Prove able to fail** (acceptance 6) — revert each wiring independently and record the split:
   COPY-only revert, parameter-only revert (must fail the non-UTC test), and the TimescaleDB inheritance check.

10. **Document** — § Conventions entry in the aggregator `CLAUDE.md` (this is a framework-wide rule about what
    a `DateTime` column means, not a connector detail) + the PostgreSQL project's `CLAUDE.md`.

### Files

- `Birko.Data.SQL.PostgreSQL/Database/Connectors/PostgreSQLConnector.cs` — helper, `:308`, `:425`, `:478`
- `Framework.Tests/Birko.Data.SQL.PostgreSQL.Tests/BulkTransactionBoundaryLiveTests.cs` + new test file
- `Framework.Tests/Birko.Data.TimescaleDB.Tests/BulkTransactionBoundaryLiveTests.cs` + new test file
- `Birko.Framework/CLAUDE.md` § Conventions; `Birko.Data.SQL.PostgreSQL/CLAUDE.md`

Three repos → three commits, production first (§ Task tracking › Integration model).

### Risks

- **The step-2 premise is load-bearing and undefended by the compiler.** [[TASK-263]] lands a tz-aware arm and
  makes "every bound `DateTime` targets `TIMESTAMP`" false. Mitigated by the comment, by the read-back-`Kind`
  test, and by naming this helper in TASK-263's own scope.
- **A UTC server cannot detect the silent half**, so the dedicated non-UTC database is the only thing standing
  between this fix and a step-4 revert that passes.

### Split signals — spawned, not folded in

- **[[TASK-263]]** — opt-in timezone-aware column type. `ConvertType` already maps
  `DbType.DateTimeOffset → TIMESTAMPTZ` (PostgreSQL) and `DATETIMEOFFSET` (MSSql), with MySQL `DATETIME` and
  SQLite numeric as the fallbacks — but **nothing can reach it**: `CreateAbstractField` has no
  `DateTimeOffset` arm, so such a property throws `FieldAttributeException` at table load (SH-H037), and no
  attribute can override a `DbType`. So there is currently no way to persist an instant with its offset. This
  is the escape hatch TASK-256's rule points at, and it must revisit step 2's helper.
- **The same `Kind` asymmetry on MySQL / MSSql / SQLite is unmeasured.** Probably benign — none has a tz-aware
  column in play for `DbType.DateTime` — but that is a guess, and this task's own lesson is that the silent
  cell is only ever found by measuring. Folded into [[TASK-263]]'s survey rather than spawned separately.


## Human test plan

- [ ] N/A — mechanical; the proof is a UTC-kinded bulk insert against a real PostgreSQL and the value read back.
