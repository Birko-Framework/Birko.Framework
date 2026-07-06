---
id: TASK-042
parent: STORY-039
feature: null
status: review
priority: P2
assignee: ai
created: 2026-07-06
depends-on: []
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
- [~] Tests: a DI-resolution test per provider (register → resolve → CRUD round-trip), guarded. — `Birko.Data.SQL.Providers.Tests` (7 tests, green): per-provider factory/settings/connection-string + `AddXStores` singleton resolution run offline; the **live CRUD round-trip is env-gated** (`BIRKO_{PROV}_TEST`) and skipped until a server is provided → task stays `review`.
- [x] `Recent Updates` entry added per Birko convention.

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
