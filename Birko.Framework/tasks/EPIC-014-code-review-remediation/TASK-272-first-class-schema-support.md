---
id: TASK-272
parent: EPIC-014
feature: FEATURE-014
status: done
priority: P2
assignee: ai
created: 2026-08-21
depends-on: []
blocks: []
related: [TASK-209, TASK-211, TASK-253, TASK-262]
findings: []
pr: null
github-issue: null
jira-key: null
affects: [Birko.Data.SQL, Birko.Data.SQL.MSSql, Birko.Data.SQL.PostgreSQL]
---

# An entity cannot say which schema it lives in

## Context — the design question TASK-262 deliberately did not answer

TASK-262 fixed a *regression*: `AbstractConnectorBase.QualifiedIdentifier` now splits a caller-supplied
`reporting.evts` on unquoted dots and quotes each part, so the TimescaleDB migration emitters reach a
schema-qualified object again. That covers a name a **caller** hands in as a string.

It does **not** give the framework a concept of a schema. Measured at that task's close:

- **No schema concept exists anywhere.** Not on `Attributes.Table` (which takes only `Name`), not on
  `Tables.Table` (`Name`, `Fields`, `Type`, `Indexes`), and not in any `Settings` class.
- So an entity cannot declare its schema, and every read and every DDL statement addresses the default schema
  of whatever connection the settings produced.

## Why this is table-and-connector, not one or the other

Established by measurement rather than preference:

- **Connector-only cannot express it.** A connector is cached process-wide per (type, settings id), so it can
  hold at most *one* schema. That is a **default / search path** — "everything lives in `reporting`" — which is
  a legitimate and *separate* feature. It cannot say "entity A is in `reporting`, entity B is in `public`".
- **Table-only cannot render it.** `Tables.Table` holds no connector, and quoting is provider-specific (`"`
  vs `` ` `` vs `[]`), as is whether schemas exist at all. A table rendering its own qualified name would have
  to hardcode ANSI quotes and break MySQL and MSSql.
- **The framework already has the seam.** `Table.GetSelectFields(…, Func<string,string>? quoteTable)` takes
  the quoting *in* as a delegate for exactly this reason (TASK-209). Follow that split: the table knows the
  name, the connector knows the dialect.

So the shape is: **identity on the table, rendering and capability on the connector.**

```
[Table("evts", Schema = "reporting")]      -> Tables.Table.Schema (nullable = provider default)

AbstractConnectorBase:
    SupportsSchemas                        -> capability, same family as SupportsTransactionalDdl,
                                              FoldsUnquotedIdentifiers, IsMissingTableException
    QualifiedIdentifier(schema, name)      -> one producer, per-part quoting (TASK-262 built the
                                              string-splitting half already)
```

## The per-provider reality, measured 2026-08-21

| Provider | `reporting.evts` means | True intra-database schemas |
|---|---|---|
| PostgreSQL 16.15 | schema `reporting` in the current DB (`public` and `reporting` both inside `birkoview`) | **yes**, default `public` |
| SQL Server 2022 | schema `reporting`, default `dbo` | **yes** |
| MySQL 8.4 | **a different database** — `CREATE SCHEMA reporting` created a sibling of `birkoview` | no; SCHEMA is a synonym for DATABASE |
| SQLite | nothing, beyond an `ATTACH`ed database prefix (`main.`) | no |

**So `SupportsSchemas` has two `true` and two `false` providers.** Per § Conventions that means the `false`
side must be asserted, or the capability is indistinguishable from an unconditional one and could be deleted
with no test noticing — the `FoldsUnquotedIdentifiers` lesson.

## What to decide

1. **What does `Schema` mean on MySQL?** Honouring it silently crosses *databases*, which is a different
   blast radius from a schema. Refusing (throw at table load, per § SH-H037) is probably right, but it is a
   decision to record, not to infer. "Same word, two meanings" is the trap.
2. **Is there also a default/search-path setting?** Complementary and orthogonal to per-entity qualification —
   and cheaper. It may even be the whole feature a consumer actually wants; decide before building the bigger
   one.
3. **Every sink, enumerated before any are changed.** A qualified name has to be honoured in `CreateTable`,
   `DropTable`, `SelectTableReference` (which emits `FROM "T" AS T` — note the **alias** must stay bare and
   unqualified, per TASK-211), `AddRequiredWhere`'s qualifier stripping (TASK-216), index DDL, view DDL,
   `AlterTableAdd`/`Drop`, the migrations `SqlSchemaBuilder`, and `IsHypertable`'s catalogue probe. This
   family's record is that a funnel with one missed sink is worse than no funnel.

## Acceptance criteria

- [x] A decision recorded on each of the three questions above, with reasons.
- [x] `Schema` declarable per entity and honoured end to end on **PostgreSQL and MSSql**: create, read, write,
      index, and drop an entity in a non-default schema, asserted against the catalogue and against returned
      rows — not against the absence of an exception (TASK-209).
- [x] The `false` side of `SupportsSchemas` asserted on MySQL and SQLite, so the capability is not
      indistinguishable from unconditional.
- [x] The bare-alias invariant preserved: `SelectTableReference` may qualify the *relation* but the alias must
      stay bare, or every qualifier stops resolving on PostgreSQL (TASK-211).
- [x] Every sink from question 3 either honours the schema or is recorded as deliberately not doing so.
- [x] Proven able to fail.

## Out of scope

- The caller-supplied qualified *string* in the TimescaleDB emitters — **[[TASK-262]] did that** and it is
  independent of this feature.
- Routing those emitters through the connector — [[TASK-271]].
- The connector-caching pattern — [[TASK-270]].

## Human test plan

- [x] N/A — mechanical; the proof is an entity's rows and its DDL landing in the declared schema, readable from
      the catalogue.

## Decisions (2026-10-15, by the user)

1. **Providers without schemas: a table-name prefix with `__`.** On MySQL/MariaDB (where `SCHEMA` is a separate
   database) and SQLite (no schemas), `[Table("evts", Schema = "reporting")]` becomes the table `reporting__evts` in the
   same database. On PostgreSQL, SQL Server and TimescaleDB it is `reporting.evts`. A load-time check refuses two
   entities that resolve to the same physical name (e.g. `reporting__evts` declared directly beside `evts` in
   `reporting`). *Rejected:* refusing the declaration (the same model should work on every provider), mapping to another
   MySQL database (one word, two blast radii), and ignoring it (silent).
2. **A connection-wide default schema ships in this task too.** It goes on the settings; a per-entity `Schema`
   overrides it. On providers without schemas the default becomes the same `__` prefix. *Rejected:* a separate task
   (the user prefers one migration story) and per-entity only.

These settle the plan's step 1 grill. Steps 2–5 proceed with them: the prefix is one more case of
`QualifiedIdentifier(schema, name)` on the connector, which the catalogue lookups must use too.

## Resolution (2026-10-16)

**Built as decided on 2026-10-15** (question 1: a `schema__` prefix on MySQL and SQLite; question 2: a connection-wide
default ships too). Question 3 was answered by an inventory of every table sink before any change: about 100, listed by
category in the session, all routed or recorded below.

- **Identity.** `[Table(Name, Schema = …)]`, DataAnnotations' `Schema` (read by nobody before), the cross-assembly
  rebuild, and `ModelMap.ToTable(name, schema)` → `Tables.Table.Schema`. `Name` stays the logical name and the alias. A
  name → schema registry (`DataBase.TableSchema`) refuses one logical name in two schemas, two models that would be one
  `schema__name` table, and a schema that is not a plain identifier.
- **Default.** `ISqlSchemaSettings.Schema` on `SqlSettings` and `SqLiteSettings`, part of `GetId()` (connectors are cached
  by it; an id without a schema is unchanged).
- **Rendering, one producer.** `SupportsSchemas` (PostgreSQL, TimescaleDB, SQL Server true; MySQL, SQLite false),
  `PhysicalTable`, `TableIdentifier`, `RelationLiteral`, `TableExistsArgument`. Without a schema the output is
  byte-identical, which the existing SQL-text pins confirm.
- **Routed:** DML (FROM with the bare alias kept, INSERT, UPDATE, DELETE), bulk paths on all four providers (COPY,
  SqlBulkCopy, multi-row INSERT, bulk UPDATE/DELETE), CREATE TABLE (schema created first: `CREATE SCHEMA IF NOT EXISTS`, or
  `SCHEMA_ID`+EXEC on SQL Server; SQL Server's existence guard is now `OBJECT_ID` over the relation, it was `sys.tables` by
  name in any schema), DROP, ALTER add/drop, rename (incl. `sp_rename`), index DDL (PostgreSQL drops an index by its
  schema), the four index managers' exists/list, `TableExistsSql`, stored columns for drift and repair, missing-table
  healing (MySQL and SQLite name the stored table), view DDL (aliased by the quoted logical name; SCHEMABINDING by the
  table's schema, then the default, then dbo), `create_hypertable`, and the migration schema builder (`RenameField` now
  uses the connector's statement) and data migrator.
- **Deliberately not:** a view's own name (views live in the connection's default schema), the migrations history table
  (its own `SqlMigrationSettings.Schema`), and `ViewSqlGenerator`, which has no connector and now refuses a view over a
  table with a schema.

**Measured live:** an entity in `t272rep` created, written (single and bulk), read, filtered, updated, deleted, indexed and
dropped, asserted against the catalogue on PostgreSQL, SQL Server, MySQL and SQLite; the settings default; drift clean,
index manager, existence query; healing; a join view (on-the-fly on all four, persistent on PostgreSQL, MySQL and SQLite);
the migration builder and migrator; a hypertable created in its schema on TimescaleDB. SQL Server SCHEMABINDING is asserted
on the emitted SQL.

**Red-verified, as numbers.** The new tests use the new API, so each check breaks the code while keeping it compiling.
With schema resolution switched off (`PhysicalTable` ignoring the schema), 8 of 31 store tests fail (every "lives in its
schema" and settings-default assertion), plus SQL Server views 3 of 3, the SQLite view 1 of 1 and TimescaleDB 1 of 1. The
other 23 pass, because every sink then agrees on the default schema. To prove those, a second check kept creation in the
schema and reverted the catalogue, healing, view and migration sinks: drift/index/existence 4 of 4, migrations 4 of 4,
healing 2 of 2 on MySQL and SQLite, the MySQL view 1 of 1 (plus the 5 view and hypertable tests above).

Pins: healing and the view on PostgreSQL and SQL Server pass either way, because the error names the logical table and
PostgreSQL resolves `"T272Evts"` against a qualified FROM. The capability test and the load-time refusals cover new code.

**Regression:** the 32 suites importing a changed SQL, migrations, TimescaleDB or Models.SQL project (2,744 tests), live.
Three SQL Server SQL-text pins failed (`OBJECT_ID('[T]')` instead of `OBJECT_ID('T')`); the guard now keeps the bare name
when there is no schema, and all 32 pass.

**Found and filed:** [[TASK-637]]. The migration data migrator and `RenameField` quote column names, so on PostgreSQL they
miss a PascalCase model's folded columns. Measured; it is not a schema defect, and the test here uses folded names.

## Implementation plan

Drafted 2026-10-15. **This is a design task first.** Its two open questions are decisions for the user, not for an
implementer, so the plan ends in a grill before any code.

1. **Decide** (with the user, via `/grill-me`): (a) MySQL `Schema` → refuse at table load (recommended: honouring it
   silently crosses databases); (b) whether a connector-level default schema / search path ships too, or stays
   separate. Record both in the task.
2. **Identity on the table**: `[Table(Name, Schema = …)]` → `Tables.Table.Schema` (null = the provider default);
   fluent `ToTable(name, schema)` on `ModelMap` likewise.
3. **Capability and rendering on the connector**: `SupportsSchemas` (PostgreSQL and MSSQL true, MySQL and SQLite
   false, with **both sides asserted** in tests) and `QualifiedIdentifier(schema, name)` as the one producer
   (TASK-262 built the string-splitting half).
4. **Route every table reference through it**: DML (`FROM`/`INTO`/`UPDATE`), DDL (`CREATE TABLE`, indexes, `ALTER`),
   and every **catalogue lookup** (`StoredColumnsSql`, `regclass`, `OBJECT_ID`, the index managers, the
   missing-table detection), which today assume the default schema. That last group is what TASK-620 to 623's drift
   checks inherit.
5. Tests: live PostgreSQL and MSSQL (an entity in `reporting` round-trips CRUD, drift and index ensure), MySQL and
   SQLite refusal, and a default-schema control unchanged.
