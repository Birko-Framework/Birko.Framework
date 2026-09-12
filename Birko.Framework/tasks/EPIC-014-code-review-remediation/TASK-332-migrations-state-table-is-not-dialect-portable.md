---
id: TASK-332
parent: EPIC-014
feature: FEATURE-014
status: done
priority: P0
assignee: ai
picked-by: user
created: 2026-09-12
depends-on: []
blocks: []
related: [TASK-245, TASK-247, TASK-257, TASK-260, TASK-262, TASK-264, TASK-269]
findings: []
pr: 846e826
affects: [Birko.Data.Migrations.SQL]
---

# The migration runner's own bookkeeping table could not be created on MySQL or SQL Server

Reported by consumer **Symbio** after building its full 109-table schema on live PostgreSQL 16,
MySQL 8.4.11 and SQL Server 2022 CU26 for the first time — every previous Symbio schema had only ever
been built on SQLite. PostgreSQL passed completely: all tables created, zero column drift, and a value
of each declared CLR type round-tripped through every column.

**MySQL and SQL Server both failed, and neither failure was in the consumer's model.** Both were in
Birko's own migration bookkeeping, and both fire *before a single model table is reached*. Net effect:
**the SQL migration runner worked on SQLite and PostgreSQL only.**

## The two defects — reproduced live before anything was written

`SqlMigrationStore` built **one hardcoded `CREATE TABLE`** for every dialect, and wrote it out **twice**
(sync and async).

### Defect 1 — the state table was ANSI double-quoted on every dialect

`SqlMigrationStore.cs:32` declared `string quoteOpen = "\""` (and the matching close), and
`SqlMigrationRunner.cs:46` constructed the store **without passing them** — so the default always won,
even though the runner holds the connector and therefore already knows the dialect.

Measured on MySQL 8.4.11:

```
ERROR 1064 (42000): You have an error in your SQL syntax ... near
'"__Mig_Probe" ("Version" BIGINT PRIMARY KEY, "Name" VARCHAR(255) NOT NULL, "Desc' at line 1
```

MySQL accepts `"` as an identifier delimiter only when `ANSI_QUOTES` is in `sql_mode`, which this
framework never sets.

**⚠ It was a *second* producer, not the only one.** `SqlMigrationSettings.FullTableName` quoted the
table and schema with an ANSI delimiter hardcoded in its own `protected virtual QuoteIdentifier`,
beside `AbstractConnectorBase.QuoteIdentifier`. Both were wrong on half the providers.

### Defect 2 — the DDL declared `TIMESTAMP` twice

`SqlMigrationStore.cs:266-267` (sync) and `:282-283` (async). In T-SQL `TIMESTAMP` is a deprecated
synonym for `ROWVERSION` — a binary row-version type of which a table may have **at most one**.

Measured on SQL Server 2022 CU26 (16.0.4275.2):

```
Msg 2738, Level 16: A table can only have one timestamp column. Because table
'__Mig_Probe' already has one, the column 'AppliedAt' cannot be added.
```

`TEXT` in the same statement is deprecated there too.

### ⚠ The two defects do not overlap — and that measurement changed the story

`Microsoft.Data.SqlClient` connects with **`QUOTED_IDENTIFIER ON`**, under which SQL Server *does*
accept `"` as an identifier delimiter. Measured directly: the original statement is `Msg 102 Incorrect
syntax` under `sqlcmd`'s default `OFF`, and reaches `Msg 2738` with `-I`.

So **defect 1 is MySQL-only in practice and defect 2 is MSSql-only**, which is exactly why the consumer
saw two different errors — and why fixing either alone leaves one server broken. The first probe run
pointed at the wrong conclusion; the flag is what separated them.

## The fix

| Concern | Before | After |
|---|---|---|
| Identifier quoting | ANSI `"` hardcoded in two places | `connector.QuoteIdentifier` / `QualifiedIdentifier` |
| Column types | `BIGINT` / `VARCHAR(255)` / `TEXT` / `TIMESTAMP` hardcoded | `connector.ConvertType`, via `SchemaField.For` |
| Statement copies | 2 (sync + async) | 1 (`CreateMigrationsTableSql`) |
| Connector | not passed; defaults won | **required** constructor argument |
| `FullTableName` | quoted rendering on the settings | `QualifiedTableName`, the bare identity |

**The connector is required, not defaulted better.** `quoteOpen`/`quoteClose` had **0 call sites**
across the framework, its tests and all 16 consumer repos, while the runner constructed the store one
line from the connector — § TASK-247's *a fallback nobody can reach is a second implementation that
drifts*, arriving as a parameter rather than a branch. Deleting the guessing surface is the fix.

### ⚠ The trap: do NOT unify this with the entity-DDL bare-column rule

`AbstractConnector.CreateTable` emits entity columns **bare** deliberately (§ TASK-245: a quoted column
cannot resolve the case-folded one PostgreSQL stores). Making the migrations table bare too is the
obvious unification and is **silently catastrophic**: PostgreSQL folds an unquoted identifier, so a bare
`SELECT Version` asks for `version` while every already-deployed database stores `Version` — `42703` on
every read of an existing database.

Only *which* delimiters are used changed, so **PG and SQLite DML is byte-identical** and no existing
deployment is affected. MySQL and SQL Server never managed to create this table, so they have none to
upgrade.

### One recorded behaviour change on a dialect that already worked

Routing types through `ConvertType` makes SQLite's `Version` an `INTEGER PRIMARY KEY`, i.e. a rowid
alias, rather than the previous `BIGINT`. Harmless for a version number, but real — so a full-width
version (`9007199254740993`) is asserted to round-trip exactly. Existing databases are untouched: the
table is only created when absent.

## Verification — which dialects are LIVE and which were reasoned about

**All five were verified against a live server.** Nothing here is reasoned-only.

| Dialect | Version | How |
|---|---|---|
| SQLite | 3.x, on-disk file | always runs |
| PostgreSQL | 16.15 | `BIRKO_PG_HOST`, docker |
| MySQL | 8.4.11 | `BIRKO_MYSQL_HOST`, docker |
| SQL Server | 2022 CU26 (16.0.4275.2) | `BIRKO_MSSQL_HOST`, docker |
| TimescaleDB | 2.30.0 / PG 16.15 | `BIRKO_TS_HOST`, docker — via `TimescaleDBMigrationRunner` |

`BIRKO_REQUIRE_LIVE` set throughout, so a missing server is a failure and not a skip.

```
Birko.Data.Migrations.SQL.Tests          112 passed, 0 failed, 0 skipped
Birko.Data.Migrations.TimescaleDB.Tests   88 passed, 0 failed, 0 skipped
Birko.Data.SQL.View.Migrations.Tests      14 passed, 0 failed, 0 skipped
                                         ---
                                         214 passed, 0 failed, 0 skipped
```

Build with `-p:TreatWarningsAsErrors=true`: **0 warnings, 0 errors**.

## Mutation proof — four disjoint reverts, each run live

| # | Mutation | Result |
|---|---|---|
| 1 | ANSI quoting restored | **5 red** — MySQL ×4 (both live round-trips + both offline pins), MSSql ×1 (delimiter pin). PG/SQLite green, as ANSI *is* their quoting. |
| 2 | `TIMESTAMP` restored for the datetime columns | **4 red — all MSSql**: sync round-trip, async round-trip, offline type pin, catalogue pin. |
| 3 | Async path keeps its own copy of the old statement | **2 red** — exactly `The_async_path_…` on exactly MySQL and MSSql; **every sync test green**. |
| 4 | Columns emitted bare (the rejected unification) | **5 red**, incl. PostgreSQL's upgrade test — while the **fresh-database test stayed green**. |

Mutation 3 is the reason both paths are covered: a sync-only suite would have called half a fix done.
Mutation 4 is the reason the suite carries an *upgrade* test and not only a clean-server round-trip.

## Tests added (24)

- `MigrationsTableDialectTests.cs` — an abstract base carrying the shared bodies, so the four dialects
  cannot drift apart: two offline DDL pins (quoting comes from the connector; the declared 255 survives)
  and two live round-trips (sync via `SqlMigrationRunner.Migrate`, async via the store's async API).
- `MigrationsTableDialectLiveTests.cs` — the four subclasses, plus per-dialect pins: MySQL's *no ANSI
  double quote reaches a statement*, MSSql's *no `TIMESTAMP` column type* + a `sys.columns` check, the
  PostgreSQL **upgrade** test, and SQLite's wide-version round-trip.
- `SqlMigrationSettingsTests` — `QualifiedTableName` is the bare identity, never a quoted rendering.

**The assertion is a read-back, never "the DDL did not throw".** `GetAppliedVersions` answers **empty**
for an absent table rather than failing, so an empty set cannot distinguish *created* from *never
created*; only a recorded version coming back proves the table exists, accepts a write and is queryable.

**Both sides of the string-length claim are asserted.** SQLite has no length-enforcing string type, so
its DDL correctly carries no `255` — pinned as correct (§ TASK-264), so nobody later "fixes" it into a
divergence.

## ⚠ Two process mistakes, recorded

- **`git checkout` on a file with uncommitted work destroyed the fix.** Used to undo mutation 1; the
  change was rebuilt from a script. Mutations are now reverted from an explicit backup copy.
- **35 TimescaleDB "failures" were my own `BIRKO_REQUIRE_LIVE`.** Set globally across a suite whose
  server was not running, every one was `SKIPPED: no live TimescaleDB` promoted to a failure — the trap
  § TASK-259 and § TASK-266 both record. Standing the container up gave 88/88 and a fifth live dialect.

## Out of scope — do not conflate

Three Symbio columns (`SpaceAttributes.Key`, `ProductAttributes.Key`, `InAppNotifications.Read`) are
reserved words in both MySQL and T-SQL, so their `CREATE TABLE` fails there. That is **the consumer's**
model-naming decision (their TASK-690), not a framework defect — the bare-column emission it collides
with is deliberate (§ TASK-245). Anyone reproducing this task will hit it and it looks like a third
framework bug; "fixing" it by quoting entity columns everywhere would break PostgreSQL, the one server
dialect that already worked.
