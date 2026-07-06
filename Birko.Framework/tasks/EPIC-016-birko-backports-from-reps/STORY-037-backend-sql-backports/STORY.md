---
id: STORY-037
parent: EPIC-016
status: done
created: 2026-07-06
---

# Backend / SQL framework backports (shipped)

## User story

As a **Birko consumer building an ASP.NET + SQLite service**, I want the owner-scoped CRUD,
enum/guid, schema-migration and store-wiring boilerplate to live in the framework, so that I don't
hand-repeat it (and hit the SQLite migration gotcha) in every new app.

## Behaviour

- A single `MapOwnedCrud<…>()` call replaces the ~80-line hand-written GET/POST/PUT/DELETE +
  ownership-guard block Reps repeated across 7 endpoint files.
- Enum DTO fields parse by stable name only (ordinals rejected); empty Guids normalize to null.
- A mapping-driven migration provisions a v1 schema from registered `IModelMapping<T>` instead of
  hand-written column DDL.
- SQLite hosts get a store-factory + DI extension that resolves the DB path against content root.
- SQLite migrations no longer deadlock/throw by default — the connector picks safe transaction
  behaviour and failures are observable, not just thrown.

## Completed ledger (migrated from Reps EPIC-002 / STORY-008)

Full per-task detail (context, acceptance transcript) lives in the origin tree, linked per row.
This story is a re-homed record; the code shipped.

| Landed capability | Landing site | Origin task |
|---|---|---|
| `MapOwnedCrud<TModel,TRequest,TRepo>()` | `Birko.Communication.AspNetCore/OwnedCrud/` | `Consumers/WorkoutTracker/tasks/EPIC-002-birko-backports/STORY-008-backend-framework-backports/TASK-030-owned-crud-minimal-api-mapper.md` |
| Name-only enum parse + Guid normalize | `Birko.Helpers/EnumHelper.cs`, `GuidHelper.cs` | `…/STORY-008…/TASK-031-birko-helpers-enum-guid-additions.md` |
| Mapping-driven create-tables migration | `Birko.Data.Migrations.SQL/CreateTablesMigration.cs` (base `AbstractConnector.CreateTable(Type[])`) | `…/STORY-008…/TASK-032-mapping-driven-create-tables-migration.md` |
| SQLite store-factory + DI extension (`AddSqLiteStores`) | `Birko.Data.SQL.SqLite/{Stores,Extensions}/` | `…/STORY-008…/TASK-033-sqlite-store-factory-di.md` |
| Migration transaction default + observable failure | `Birko.Data.Migrations.SQL/{SqlMigrationSettings,SqlMigrationRunner}.cs` | `…/STORY-008…/TASK-034-sqlite-migration-transaction-fix.md` |

## Follow-ups spawned by the backport review

- TASK-033's store-factory + DI pattern is **SQLite-only**; the review flagged MSSql / MySQL /
  PostgreSQL as lacking it → **STORY-039**.
- TASK-032 (create-tables) and TASK-034 (migration transaction fix) were confirmed **provider-agnostic**
  in shared code — the other SQL providers inherit them, no backport needed.
