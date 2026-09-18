---
id: TASK-259
parent: EPIC-014
feature: FEATURE-014
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: done
priority: P1
assignee: ai
created: 2026-08-18
depends-on: []
blocks: []
related: [TASK-240, TASK-242, TASK-247, TASK-253, TASK-270, TASK-271]
findings: []
pr: "Birko.Data.SQL 33fbbfc · Birko.Data.Migrations.SQL 515da6f · Birko.Data.Migrations.TimescaleDB e08dc6d · tests: Birko.Data.Migrations.SQL.Tests 7b000a8 · Birko.Data.SQL.SqLite.Tests 31b2ba2"
github-issue: null
jira-key: null
affects: [Birko.Data.Migrations.SQL, Birko.Data.SQL]
---

# `SqlSchemaBuilder` publishes its connection onto a process-wide cached connector and never clears it

Found while grilling **TASK-253**'s plan, evaluating (and rejecting) the option of routing the TimescaleDB
migration emitters through `connector.CreateHypertable` via `SetExternalTransaction`. The mechanism turned
out to be one the framework had deliberately abandoned everywhere *except* here.

## What is wrong

`Birko.Data.Migrations.SQL/Context/SqlSchemaBuilder.cs` calls
`_connector.SetExternalTransaction(_connection, _transaction)` at **three** sites — `:133`
(`EnsureExternalTransaction`, itself called from five methods), `:218` and `:286` — and **never calls it
again with nulls**. It is the only remaining caller in the framework.

`AbstractConnector.SetExternalTransaction` (`:215`) stores the pair on the connector, and
`DoCommand` (`:186`) / `DoCommandWithTransaction` (`:232`) check
`ExternalConnection != null && ExternalTransaction != null` and route the command onto it. Connectors are
cached **process-wide per (type, settings id)** by `DataBase.GetConnector`.

So once any migration has run, the shared connector retains that migration's connection and transaction
for the life of the process. Every subsequent command from any store against the same database — reads
included — takes the `ExternalConnection` branch onto a connection the migration runner has since
disposed.

**Both stores already carry the explanation, as a reason they stopped doing this:**

> *"Replaces the former `Connector.SetExternalTransaction` call, which published one caller's transaction
> onto a connector cached process-wide per (type, settings id) — i.e. onto every concurrent caller against
> the same database."* — `Birko.Data.SQL/Stores/DataBaseStore.cs:45`

> *"It deliberately no longer calls `Connector.SetExternalTransaction`: connectors are cached process-wide
> per (type, settings id), so that call published one caller's transaction to every concurrent caller
> against the same database."* — `Birko.Data.SQL/Stores/AsyncDataBaseStore.cs:48`

TASK-240 replaced it with `AmbientSqlTransaction`, which is scoped to the async flow and restores itself.
The schema builder was not migrated with them. § Conventions records the pair as *"deliberately **not**
suppressed: its only user is the migrations `SqlSchemaBuilder`, which exists to run DDL in a transaction it
owns"* — which explains why it is *read*, and says nothing about it never being cleared.

## Measure before fixing — this may be entirely latent

Do this first and record the answer as a number, because it decides whether this is a live data-integrity
bug or dead code:

- **Is `SqlSchemaBuilder` reachable from any consumer?** TASK-247 swept all 16 consumer repos and found
  **0** uses of `ISchemaBuilder` and 0 migration-declared indexes. If that still holds, nothing calls this
  in production and the fix is hygiene. Re-run the sweep rather than citing it — TASK-247 closed today and
  the claim is worth one command.
- **Does the stale pair actually survive the runner?** Check whether `SqlMigrationRunner` disposes or
  replaces the connector, and whether anything resets connector state between migrations. It is possible
  the runner constructs a connector that is never the cached one, in which case the blast radius is one
  object.
- **Is a disposed `DbConnection` on that branch loud or silent?** An `ObjectDisposedException` far from the
  migration is bad; Npgsql silently reopening a pooled connection with no transaction would be worse — a
  write that believes it is in a transaction and is not, which is the whole family TASK-240/242 exists to
  close.

State plainly which of firing / latent it is. TASK-246 had to be corrected after its commit landed for
claiming live impact it did not have.

## Measurement — done 2026-08-21. Verdict: **FIRING on the default configuration**

Not latent. Reproduced end to end on SQLite: a migration whose `Up` calls `context.Schema.CreateCollection(…)`,
run through `SqlMigrationRunner` with **default settings**, against a connector from `DataBase.GetConnector`:

```
ExternalConnection null?   False
ExternalTransaction null?  False
connection state = Closed          <- disposed by the runner's `using`
store.Read() THREW Exception: CREATE TABLE IF NOT EXISTS "LeakWidgets" (...)
```

**The consequence is worse than this task predicted.** It is not merely that a subsequent command takes the
`ExternalConnection` branch: the first thing a store does is its **lazy schema-ensure**, so the `CREATE TABLE
IF NOT EXISTS` is routed onto the dead connection and throws. Per § Conventions (*"anything that throws in
there leaves the store permanently uninitialised and re-throws on every later operation — reads included"*),
the store is dead for the **life of the process**, for that entity. One migration poisons every store sharing
the (type, settings id).

### The three questions this task asked, answered

1. **Is `SqlSchemaBuilder` reachable from a consumer? Yes — and TASK-247's "0 uses" is stale.** Re-running the
   sweep (which this task rightly demanded rather than citing) found **2** hits, and reading both is what
   matters: `Symbio.Tests.Unit/MigrationRuntimeTests.cs:85` genuinely uses
   `context.Schema.CreateCollection(...).Build()`; `WorkoutTracker/Reps.Domain/Storage/InitialSchemaMigration.cs`
   only *mentions* `ISchemaBuilder` in a comment explaining that it deliberately avoids it (and that comment is
   now itself stale — TASK-247/CR-C14 made `Build()` a terminal on the interface).
2. **Does the stale pair survive the runner? Yes, and both objects are dead.** `ExecuteMigrations` holds
   `using var connection = _connector.CreateConnection(...)` and `ExecuteWithTransaction` holds
   `using var transaction = connection.BeginTransaction()`; both are published via the three
   `SetExternalTransaction` calls and neither is ever cleared. The runner itself is unaffected because it makes
   its own connection rather than going through `DoCommand` — which is exactly why nothing noticed.
3. **Loud or silent? Loud, but misattributed.** It throws, so it is not the silent-wrong-answer case that
   would have been worse. But `InitException` re-wraps it as a bare `Exception` whose *message is the DDL
   statement*, arriving at a caller doing an ordinary read, far from the migration that caused it.

### Why no shipped consumer hits it — configuration, not design

The guard is `ExternalConnection != null && **ExternalTransaction != null**` — both. `SqlSchemaBuilder`'s
transaction is nullable, so `UseTransaction = false` leaves `ExternalTransaction` null and the branch is never
taken. Both production consumers set exactly that, for reasons unrelated to this defect:

| Caller | `UseTransaction` | Uses `context.Schema`? | Exposed? |
|---|---|---|---|
| Symbio `ProviderMigrationRunnerFactory:56` (all four SQL providers) | **false** — "load-bearing, not a default we inherited by accident" | its tests do | no |
| WorkoutTracker `RepsMigrator:38` + all its tests | **false** | no — uses `connector.CreateTable` directly | no |
| Framework `SqliteMigrationRunnerTests` / `SqlScriptMigrationTests` | **true (default)** | **yes**, line 57 | yes — but each test uses a unique temp path, so a unique cache entry, so the poison never crosses tests |
| Framework `SqlSchemaBuilderTests` | n/a — constructs the builder directly with `transaction: null` | yes | no |

So: **firing on the default configuration, and invisible today only because the one consumer that uses
`ISchemaBuilder` disables transactions for an unrelated reason, and the framework's own tests each get their
own cache entry.** A consumer that leaves `UseTransaction` at its default and uses `context.Schema` gets a
permanently dead store. That is a one-line configuration away, and the default is the dangerous value.

## Acceptance criteria

- [x] The measurement above recorded in this file as numbers, and the firing-or-latent verdict stated
      before any fix is written. → § Measurement: **firing on the default configuration**.
- [x] `SqlSchemaBuilder` no longer leaves the pair set past its own use.
      → **Migrated onto `AmbientSqlTransaction` outright**, so the legacy pair lost its last caller. One
      `internal static SqlSchemaBuilder.EnterAmbientBoundary(connector, connection, transaction)` producer,
      called from all three former `SetExternalTransaction` sites via `using var boundary = …`. It returns
      **null when there is no transaction**, which reproduces the old behaviour exactly: the legacy branch
      required both properties non-null, so `UseTransaction = false` never routed connector commands onto the
      migration's connection either.
- [x] Deleted or kept, and why. → **Deleted**: the setter, both properties, and its four read branches in
      `DoCommand` / `DoCommandWithTransaction` / `RunBulk` / `RunReaderCommand`. TASK-247's rule applied to a
      boundary mechanism — a mechanism nobody can reach is a second implementation that drifts, and this one
      was the exact trap just closed, left public and reachable. Measured **0** production callers across all
      16 consumer repos before removing. `RunReaderCommandWithExternalTransaction` **survived** (the ambient
      path shares it) and was renamed `RunReaderCommandOn`, since it no longer serves anything "external".
      One in-tree test needed rewriting rather than deleting: `TransactionBoundaryEndToEndTests` asserted on
      the now-absent properties — TASK-240's pin, whose subject is gone and whose *claim* is still covered,
      more strongly, by the `AmbientSqlTransaction.Current` assertion after its `Task.Run`.
- [x] § Conventions' sentence updated. → It *was* a blessing of the status quo, and the status quo was this
      defect; it now records that there is no second thing to suppress ("one mechanism, one rule"). A new
      § Conventions entry states the general rule, and `TimescaleDBMigration`'s comment — which cited this
      mechanism as the reason TASK-253 did **not** route its emitters through the connector — now records that
      the constraint is gone and the choice reopened.
- [x] Proven able to fail. → `SchemaBuilderBoundaryLeakTests` (2 tests). Reverting the single producer to
      `SetExternalTransaction` fails **2 of 49**. Both go red, and the second is the informative one: the
      `UseTransaction = false` test *also* fails, because the old code set `ExternalConnection` even with a
      null transaction — so the connection was leaked on **every** configuration and only the consequence was
      gated. ⚠ The first attempt at this revert failed **0**, because the helper had been written three times
      and the migration path runs through the nested collection builder; see § Conventions on why it is now
      one producer.
- [x] The four provider live suites run, not just built. → See § Verification: five live servers, eight
      suites, `BIRKO_REQUIRE_LIVE` set throughout so nothing skipped.

## Verification

Live **SQL Server 2022 (16.0.4265.3)**, **PostgreSQL 16**, **MySQL 8.4**, **TimescaleDB 2 / PG16** and on-disk
SQLite, with `BIRKO_REQUIRE_LIVE` set throughout so no gated suite silently skipped. **1,165 passed, 0 failed**
across eight suites; **2 new**.

| Suite | Result |
|---|---|
| `Birko.Data.Migrations.SQL.Tests` | 49 (was 47) |
| `Birko.Data.Migrations.Tests` | 17 |
| `Birko.Data.Migrations.TimescaleDB.Tests` | 48 |
| `Birko.Data.SQL.Tests` | 575 |
| `Birko.Data.SQL.SqLite.Tests` | 229 |
| `Birko.Data.SQL.MySQL.Tests` | 78 |
| `Birko.Data.SQL.PostgreSQL.Tests` | 82 |
| `Birko.Data.SQL.MSSql.Tests` | 87 |

> ⚠ **Two measurement traps hit during this task, recorded rather than smoothed over.**
> 1. Running with `BIRKO_REQUIRE_LIVE=1` reported **14 failures** in
>    `Birko.Data.Migrations.TimescaleDB.Tests` — there was no TimescaleDB running, and the flag turns a skip
>    into a failure. 48/48 without it. Fixed by starting the container, not by narrowing the flag. This is the
>    exact trap TASK-257 documented one task earlier, hit again by the person who wrote it down.
> 2. One **unreproducible** failure in `Birko.Data.SQL.SqLite.Tests` (228/229) on a single run, in the
>    transaction-boundary suite. Green on four subsequent runs; the suite uses file-backed SQLite databases, so
>    a temp-file/lock timing flake is the likely cause. Recorded because a flake in *that* suite while changing
>    *this* mechanism is worth someone knowing, not because it was diagnosed.

## Out of scope

- The TimescaleDB migration emitters' identifier quoting and folding — **[[TASK-253]] owns those**, and it
  deliberately does *not* route them through the connector precisely because of this defect.
- `AmbientSqlTransaction`'s own semantics. TASK-240/242/243 settled them; this task either reuses it or
  scopes the legacy pair, and changes neither.
- Whether schema-ensure belongs in a caller's unit of work at all — **[[TASK-244]] owns that**, still open.
- **Routing the TimescaleDB migration emitters through the connector, now that the mechanism that forbade it
  is gone** — [[TASK-271]] owns it. TASK-253 rejected it *because* it required `SetExternalTransaction`; with
  that deleted and `AmbientSqlTransaction` available it is a real option, letting those emitters reuse
  `DoDdlCommand` and the provider capabilities. Not done here: it is a behaviour change on a live TimescaleDB
  path and wants its own measurement.
- **The pattern that produced this defect three times** — [[TASK-270]] owns it. `DataBase.GetConnector` caches
  a connector process-wide, and per-caller state has now been put on it by the stores (TASK-240), the
  index-failure reporting (keyed) and this task. Audited at this close: no *firing* defect remains, but
  `RetryPolicy` is publicly settable on the shared object (0 assignments measured), `IsInitializing` is
  mutable, and the four events accumulate handlers unless callers unsubscribe.

## Human test plan

- [x] N/A — mechanical; the proof is the connection identity a post-migration command runs on, which
      `SchemaBuilderBoundaryLeakTests` asserts. Nothing here needs human judgement or hardware.
