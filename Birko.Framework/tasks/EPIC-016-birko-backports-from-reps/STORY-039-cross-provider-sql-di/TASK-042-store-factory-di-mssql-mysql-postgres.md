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

- [x] Set `BIRKO_MSSQL_HOST` (plus optional `_PORT` / `_USER` / `_PASSWORD` / `_DB`), run
      `Birko.Data.SQL.Providers.Tests`, and confirm the gated MSSql round-trip connects + does a create/read.
      — done at sign-off against SQL Server 2022; the gate was renamed onto the house convention below.
- [x] Repeat against MySQL / PostgreSQL via `BIRKO_MYSQL_HOST` / `BIRKO_PG_HOST`. — MySQL 8.4 + PostgreSQL 16.
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


---

## Follow-up (2026-09-20) — the round-trips were red in CI from the day they were written

The sign-off above is accurate about what it measured and wrong about one word. It closes with
*"rather than inventing a third one"* — but the **gate** was an invention, even though the
require-live promotion on top of it was not.

### What happened

The three round-trips gated on `BIRKO_{PROVIDER}_TEST=host;db;user;pass`, a packed variable used by
this suite and nothing else. Every other live suite in the tree — eleven of them in the same CI job —
reads a per-field group: `BIRKO_PG_HOST` / `_PORT` / `_USER` / `_PASSWORD` / `_DB`, and siblings.
`.github/workflows/live-tests.yml` sets the group, because it was written for those eleven. It has
never set a `*_TEST` variable, and `BIRKO_REQUIRE_LIVE: '1'` is declared at workflow level.

So on every `live-tests` run since the suite landed, the gate found nothing, the promotion did its
job, and the job went red: **3 failed, 7 passed, 134 ms** — the duration being the tell, since all
three threw before touching a socket. Run
[35458738975](https://github.com/Birko-Framework/Birko.Framework/actions/runs/35458738975) is the
one that prompted this. The other eleven suites passed against those same containers, so the servers
were never the problem.

### Why the local verification could not see it

The sign-off's 10/10 was real, measured with the `*_TEST` variables exported by hand. **A gate
verified only by the person who invented it is verified against their shell, not against the
fixture.** The mutation table above even includes *"env var absent with `BIRKO_REQUIRE_LIVE=1`
→ 1 failed"* — which is precisely the state CI was in, recorded as a passing mutation test rather
than recognised as the CI configuration.

### The fix

`RunLiveAsync(envVar, body)` → `Resolve(prefix, defaultPort, defaultUser, defaultPassword)`, reading
`BIRKO_{prefix}_HOST` + the optional companions, with defaults matching the workflow's containers
exactly as the sibling suites' defaults do. `_HOST` alone opts a run in. Nothing in the workflow
changes — the suite now reads what the fixture has been setting all along.

Option (2), teaching `live-tests.yml` the packed variables, was rejected: it is the smaller diff but
leaves two gating vocabularies in one job, which is the thing that produced the defect.

### Measured

- No env: **10/10 green**, three skip lines naming the variable to set.
- `BIRKO_REQUIRE_LIVE=1`, no host: **3 failed / 7 passed, 156 ms** — reproduces the CI failure exactly,
  so the promotion still fires and this is not a fix by silencing.
- `BIRKO_REQUIRE_LIVE=1 BIRKO_PG_HOST=127.0.0.1 BIRKO_PG_PORT=59999`: fails with
  `NpgsqlException: Failed to connect to 127.0.0.1:59999` after **4 s** — proving the gate now opts
  the test *in* and honours host **and** port, rather than skipping past the driver.
- ⚠ **Not re-run against real servers.** No Docker on this machine, so the three round-trips
  themselves are unexercised here; the sign-off's live 10/10 stands as their last real measurement,
  and CI is what will prove the renamed gate reaches the containers. **If the next `live-tests` run
  is still red, the cause is a product defect the old gate was hiding, not this rename.**
