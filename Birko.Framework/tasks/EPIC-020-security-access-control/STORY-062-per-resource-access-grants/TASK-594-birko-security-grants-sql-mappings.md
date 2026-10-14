---
id: TASK-594
parent: STORY-062
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: done
# blocked: <reason> — add this line while the task is blocked, keeping its status; /tasks unblock removes it
priority: P2
assignee: ai
created: 2026-10-14
depends-on: []
blocks: [TASK-593]
# findings: ids this task remediates — from a review/audit/harvest/drill pass, or from ordinary
# field use with no pass behind it at all. Prefixes: see /tasks intake
findings: []
pr: null
github-issue: null
jira-key: null
---

# Birko.Security.Grants.SQL — fluent SQL mappings for ResourceGrant and Invitation

## Context

Spawned 2026-10-14 when TASK-593 was picked. **Grants cannot be stored on any SQL backend today.** `Birko.Data.SQL`
builds a table only from a `[Table]` attribute or a registered table name (`DataBase.RegisterTableName`). It takes
its columns from `Birko.Data.SQL.Attributes.*` or from a fluent mapping. `ResourceGrant` and `Invitation` carry no
SQL attributes, by design: the core project depends only on Data.Core, Data.Stores and Time.Abstractions. No
mapping ships, so a SQL consumer (Reps, the presentation project, Symbio) would have to write one.

Schemaless backends (InMemory, JSON, XML, Mongo, Raven, Cosmos, ES) need nothing; SQL needs a schema. The user
chose the framework's established pattern for this: a sibling `*.SQL` project of `IModelMapping<T>` classes (as
`Birko.Models.Users.SQL` does for `User`, `Role` …), over putting SQL attributes on the core models (which would break
their layering) or teaching `Birko.Data.SQL` to map plain models (a framework-wide change).

⚠ **Measure before relying on it.** `Birko.Models.SQL`'s own CLAUDE.md says `HasMaxLength` / `HasPrecision` /
`HasScale` / `HasIndex` are "mapping metadata only (not applied)". `ApplyToDatabase` applies the table name, column
name and primary / unique / required / auto-increment flags. `Birko.Models.Users.Role` has no SQL attributes and is
mapped, which suggests plain properties do get columns, but that has to be shown on SQLite, not assumed. The indexes
the grant lookups want, (UserGuid, ResourceType) and the exact key, may not be expressible through the fluent API
today. If so, record it and either use what the API does apply or spawn the `Birko.Models.SQL` gap. Never declare an
index that is silently dropped (rule 51).

## Acceptance criteria

- [x] New shared project `Birko.Security.Grants.SQL` (`.shproj` + `.projitems`, `CLAUDE.md`, `README.md`), with
      `IModelMapping<ResourceGrant>` and `IModelMapping<Invitation>`. Its dependencies are Grants + Birko.Models.SQL,
      following `Birko.Models.Users.SQL`. It is registered in `.slnx`, `.code-workspace`, the aggregator `.csproj`,
      `CLAUDE-projects.md` (catalog + Dependency Flow), `README.md` and `docs/security.md`.
- [x] Measured on SQLite: after registering the mappings and calling `ApplyToDatabase`, an `AsyncSQLiteStore<ResourceGrant>`
      and an `AsyncSQLiteStore<Invitation>` create their tables with **every** model property as a column, and a written
      grant and invitation read back field for field: nullable Guids, the nullable `DateTime`s, and `Email`.
- [x] Table names are namespaced so they cannot collide with an app's own tables, e.g. `BirkoResourceGrants` /
      `BirkoInvitations`; the README states them.
- [x] Whatever the mapping declares is applied, or not declared. Each index or length either reaches the DDL
      (asserted against `sqlite_master`), or is absent with a line saying why. A fluent-API gap found here is spawned
      against `Birko.Models.SQL`.
      _Measured: `IsRequired` and `IsUnique` are applied, but the fluent `HasIndex` is applied by nothing → [[TASK-595]]
      (FIELD-033). The three lookup indexes are created through the provider's `IIndexManager`
      (`GrantSqlIndexes.EnsureAsync`), and asserted with `PRAGMA index_info`._
- [x] A thin store pair a SQL consumer can register as the grants stores (e.g. `SqlResourceGrantStore :
      AsyncSQLiteStore<…>, IResourceGrantStore`), or a documented one-liner per provider. Chosen and stated in the
      README with the registration code.
- [x] Tests (a new `tests/Birko.Security.Grants.SQL.Tests` with the SQLite imports, kept out of the dependency-light
      `Birko.Security.Grants.Tests`): table and column creation, round trip, and the DDL assertions.

## Out of scope

- The behavioural check of every grant query, the invitation claim and the `PropertyUpdate` / `[UtcField]` path on
  SQL: [[TASK-593]], which runs on this task's mapping.
- MSSQL / PostgreSQL / MySQL live suites: the mapping is provider-neutral, and a provider-specific gap found later is
  its own task.
- Migrations (`Birko.Data.Migrations.SQL`) for the two tables: a consumer's call, until one asks.

## Human test plan

N/A — fully covered by automated tests. Whether the tables and columns exist, and what reads back, is asserted
against the database itself.

## Implementation plan

_Drafted at pick, 2026-10-14._

1. **Measure first.** In the new test project, register `IModelMapping`s for both models, `ApplyToDatabase`, and
   `CreateSchemaAsync` an `AsyncSQLiteStore<T>`. Read `sqlite_master` and `PRAGMA table_info`: are all properties
   columns? Do `HasIndex` / `HasMaxLength` reach the DDL? Round-trip one entity of each.
2. **Shape the mapping on what was measured.** Namespaced table names, primary key `Guid`. Declare only what is
   applied; anything the fluent API drops is recorded, and spawned against `Birko.Models.SQL` if it matters
   (the lookup indexes do).
3. **Stores.** `SqlResourceGrantStore<DB>` / `SqlInvitationStore<DB>` over `AsyncDataBaseBulkStore<DB, T>`, so one
   pair serves every SQL provider, if that type composes; otherwise per-provider one-liners in the README.
4. **Project and registrations:** shproj / projitems, CLAUDE.md and README, slnx, workspace, aggregator,
   CLAUDE-projects (catalog + flow), README rows, docs/security.md, the maintenance folder list.
5. **Tests:** schema, round trip, and the DDL assertions. Run them, then `/verify-birko-conventions`, and commit
   framework + Sandbox.
