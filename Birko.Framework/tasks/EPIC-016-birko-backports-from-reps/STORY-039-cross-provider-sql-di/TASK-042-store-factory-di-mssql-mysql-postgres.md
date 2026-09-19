---
id: TASK-042
parent: STORY-039
feature: FEATURE-016
status: done
priority: P2
assignee: ai
created: 2026-07-06
depends-on: [STORY-042]
blocks: []
pr: null
github-issue: null
jira-key: null
---

# Backport store-factory + DI extension to MSSql / MySQL / PostgreSQL

## Context

Reps' TASK-033 gave `Birko.Data.SQL.SqLite` a store factory + `IServiceCollection` extension
(`Stores/SqLiteStoreFactory.cs`, `ISqLiteStoreFactory.cs`, `SqLiteStoreFactoryOptions.cs`,
`Extensions/SqLiteServiceCollectionExtensions.cs` → `AddSqLiteStores(...)`), so a host wires all
model-mapped stores in one call. The 2026-07-06 backend backport review confirmed the other three
SQL providers do **not** have this: `Birko.Data.SQL.{MSSql,MySQL,PostgreSQL}` contain `Stores/`
(`Store`, `AsyncStore`, `Settings`) only — no factory, no DI extension, no `Extensions/` folder.

This backports the **pattern**, not a verbatim copy. The SQLite factory does content-root path
resolution + eager `Directory.CreateDirectory` because SQLite is file-based
(`SqLiteStoreFactory.cs:29-38`); server providers have no such path logic. Each provider instead
builds its connection-string settings type, which already exists (`MSSqlSettings` / `MySqlSettings` /
`PostgreSqlSettings`). The create-tables migration (TASK-032) and the migration-transaction fix
(TASK-034) are already shared across providers — do **not** touch them here.

Follow the `Birko.Data.SQL.SqLite` layout as the reference and keep naming symmetric.

## Acceptance criteria

- [x] `Birko.Data.SQL.MSSql` gains a store factory + `Extensions/MSSqlServiceCollectionExtensions.cs`
      exposing `AddMSSqlStores(...)`, mirroring the SQLite shape (factory + options + interface), minus file-path logic.
- [x] `Birko.Data.SQL.MySQL` gains the same via `AddMySqlStores(...)`.
- [x] `Birko.Data.SQL.PostgreSQL` gains the same via `AddPostgreSqlStores(...)`.
- [x] Each uses the provider's existing `*Settings` connection-string type; no SQLite path logic leaks in. — options carry server/db/user/port/flags; factory builds `{MSSql,MySql,PostgreSql}Settings`.
- [x] `.projitems` updated for each of the three projects (new files compiled). — verified by building `Birko.Data.SQL.Providers.Tests`.
- [x] Tests: a DI-resolution test per provider (register → resolve → CRUD round-trip), guarded. — **done 2026-09-19 against live servers; see the sign-off.** The original `[~]` — `Birko.Data.SQL.Providers.Tests` (7 tests, green): per-provider factory/settings/connection-string + `AddXStores` singleton resolution run offline; the **live CRUD round-trip is env-gated** (`BIRKO_{PROV}_TEST`) and skipped until a server is provided → task stays `review`.
- [x] `Recent Updates` entry added per Birko convention.

## Depends on — [[STORY-042]] (integration-test tier)

The one remaining criterion (the live CRUD round-trip) is blocked on the same missing capability
STORY-042 exists to build — specifically its **`TASK-042-00` Testcontainers harness** (the MSSql/Postgres
fixture in cluster 1), **not** the whole tier. When that harness lands, adopt it here: replace the
bespoke `BIRKO_{PROV}_TEST` env gate with the shared fixture, run the round-trip, and move this task
`review` → `done`.

## Out of scope

- Any change to `CreateTablesMigration` / `SqlMigrationRunner` / `SqlMigrationSettings` (already shared).
- Non-SQL providers (Mongo/Raven/Cosmos/etc.) — a separate consideration if ever needed.

## Discovered → fixed in TASK-051

- **`MSSqlStore<T>.SetSettings(RemoteSettings)` was lossy** — it rebuilt a `PasswordSettings` keeping only
  Location/Name/Password, dropping UserName/Port/MultipleActiveResultSets/etc. **Fixed in TASK-051** (now
  passes full settings, mirroring `AsyncMSSqlStore` + the MySQL/PostgreSQL sync stores). The factory still
  hands out the async store via `GetAsyncStore<T>()` (async is the right default for a server DB).

## Human test plan

- [ ] **(pending — needs a live server)** Set `BIRKO_MSSQL_TEST=host;db;user;pass`, run
      `Birko.Data.SQL.Providers.Tests`, and confirm the gated MSSql round-trip connects + does a create/read.
- [ ] Repeat against MySQL / PostgreSQL via their env vars.
- [x] Confirm no `Directory.CreateDirectory` / content-root path code is present in the three new extensions. — verified: server options carry no path logic.

## Implementation plan

_Populated by `/tasks plan TASK-042` — leave empty until then._


---

## Signed off (2026-09-19)

**Closed as done**, and the one open criterion turned out to be open in a worse way than `[~]`
suggested.

### The criterion was ticked against a test that did not test it

The "live CRUD round-trip" was:

```csharp
factory.GetConnector().Should().NotBeNull();
```

That constructs an object. It opens no connection, creates no table, writes no row — **it passes with
every database on the machine stopped**, which is how a criterion reading *"live CRUD round-trip"*
stayed gated-but-ticked for eleven weeks without one ever happening. And the gate itself was a bare
`return`, so an absent env var was indistinguishable from a server that ran: the suite reported
"7 passed" either way.

**MySQL and PostgreSQL had no live test at all** — only MSSql, and only the vacuous one.

### What it does now, and what that measured

`RoundTripAsync` creates the table, writes a row, **reads it back by value** and deletes it. Reading
back by `Name` rather than by the returned id is deliberate: an echoed id proves only that it was
echoed, while a filter forces the value through the provider's own parameter binding and out through
its reader — which is where the per-provider column typing this factory selects actually shows up.

Run against live **SQL Server 2022 (16.0.4275.2)**, **MySQL 8.4** and **PostgreSQL 16** with
`BIRKO_REQUIRE_LIVE=1`: **10/10**.

**Verified against the servers' own catalogues rather than the green tick** — `TASK042_RoundTrip`
exists on all three, and `SELECT COUNT(*)` is **0** on all three, so the rows were written, read and
cleaned up rather than the test having quietly done nothing:

| | table created | rows left |
|---|---|---|
| PostgreSQL 16 | yes | 0 |
| MySQL 8.4 | yes | 0 |
| SQL Server 2022 | yes | 0 |

**Two mutations, both red** — which is the part the old test could not have produced:

- wrong password → **1 failed** (the old assertion would have passed, since it never connects)
- env var absent **with** `BIRKO_REQUIRE_LIVE=1` → **1 failed** (the old bare `return` passed silently)

Offline with neither set: 10/10 green, and each of the three now **writes its skip to the test
output** naming the variable to set. That follows the convention the framework's other live suites
already use (`BIRKO_REQUIRE_LIVE` promotes a skip to a failure), rather than inventing a third one.

### On the stated dependency

The task was blocked on **[[STORY-042]]**'s Testcontainers harness. That is a means, not the end: the
criterion asks for a round-trip against a live server, and one has now happened against all three.
Adopting the shared fixture when it lands remains worth doing — it replaces three bespoke env vars —
but it is **tidying an answered question**, not the answer. Recorded so the dependency is not read as
still blocking.
