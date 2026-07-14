---
id: TASK-058
parent: null
feature: null
status: done  # todo | in-progress | review | blocked | done | cancelled
priority: P2
assignee: ai
created: 2026-07-14
depends-on: []
blocks: []
pr: null
github-issue: null
jira-key: null
---

# SqLiteConnector emits invalid AUTOINCREMENT DDL for non-primary-key increment fields (dual-key models)

## Context

Surfaced while working **CR-M166** (STORY-028 cluster 1). Not an audit finding — a genuine framework
bug discovered during that work.

`SqLiteConnector.FieldDefinition` (`Birko.Data.SQL.SqLite/Database/Connectors/SqLiteConnector.cs:143`)
builds each column definition by appending constraints in a fixed order:

```csharp
result.Append(field.Name);
result.AppendFormat(" {0}", ConvertType(field.Type, field));
if (field.IsPrimary)      result.AppendFormat(" PRIMARY KEY");
if (field.IsUnique)       result.AppendFormat(" UNIQUE");
if (field.IsNotNull)      result.AppendFormat(" NOT NULL");
if (field.IsAutoincrement) result.AppendFormat(" AUTOINCREMENT");   // <-- unconditional + detached
```

This violates SQLite's grammar in **two** ways:

1. **AUTOINCREMENT on a non-PK column.** SQLite only accepts `AUTOINCREMENT` as part of an
   `INTEGER PRIMARY KEY AUTOINCREMENT` column constraint. Here it is emitted for **any**
   `IsAutoincrement` field regardless of `IsPrimary`.
2. **AUTOINCREMENT detached from PRIMARY KEY.** Even for a PK column, it is appended *after*
   `UNIQUE` / `NOT NULL`, but SQLite requires `AUTOINCREMENT` to immediately follow `PRIMARY KEY`.
   So `... PRIMARY KEY UNIQUE NOT NULL AUTOINCREMENT` is a syntax error too (a plain
   `INTEGER PRIMARY KEY AUTOINCREMENT` with no other constraints happens to be valid, which is why
   most single-Guid-PK Birko models never hit this).

### Repro (the case that blocks CR-M166)

`Birko.Data.Sync.Sql/Models/SqlSyncKnowledgeItem.cs` is a **dual-key** model:

```csharp
[PrimaryField]  public override Guid? Guid { get; set; }   // the primary key
[IncrementField] public int Id { get; set; }               // a separate, non-PK auto-increment
```

`FieldDefinition` emits `Id INTEGER NOT NULL AUTOINCREMENT` (no `PRIMARY KEY`), so
`SqLiteConnector.CreateTable` throws a SQLite syntax error. That is exactly why the CR-M166 SQL-sync
CRUD round-trips could not run on SQLite and were pushed to the MSSql/Postgres (Docker) pile.

## The design question (why this is a task, not a one-line fix)

SQLite has **no per-column auto-increment for a non-PRIMARY-KEY column** — the `AUTOINCREMENT` keyword
is inseparable from `INTEGER PRIMARY KEY`. So a model that wants a separate non-PK `[IncrementField]`
(alongside a different PK) cannot be expressed 1:1 in SQLite. A decision is needed:

- **(a) Emit valid DDL, drop the keyword.** For a non-PK increment field, emit the column as plain
  `INTEGER` (no `AUTOINCREMENT`) and rely on the application/`CreateKnowledgeItem` to populate it, or
  document that non-PK auto-increment is a no-op on SQLite. Lowest-risk; makes dual-key models
  creatable. *(Semantics differ from MSSql/Postgres, which do support non-PK identity/serial — must
  be documented.)*
- **(b) Reject with a clear error.** Throw a descriptive `NotSupportedException` at DDL time
  ("SQLite does not support AUTOINCREMENT on a non-primary-key column: {table}.{field}") instead of a
  raw syntax error, so consumers fail fast with a useful message.
- **(c) Map to rowid.** SQLite's implicit `rowid` already auto-increments; a non-PK increment field
  could be documented as advisory. (Doesn't give the column real auto-increment on insert.)

Recommend **(a)** — it unblocks CR-M166 and matches "make the common thing work"; couple it with the
PK-ordering fix below.

Independently of the non-PK question, fix defect #2: when a column **is** the PK and autoincrement,
emit `INTEGER PRIMARY KEY AUTOINCREMENT` as one adjacent clause (and ensure the type is exactly
`INTEGER` — SQLite requires it for AUTOINCREMENT), before other constraints.

## Scope

- `Birko.Data.SQL.SqLite/Database/Connectors/SqLiteConnector.cs` — `FieldDefinition` (and check
  `ConvertType` emits `INTEGER` for an autoincrement PK, not `INT`/`BIGINT`).
- Confirm the sibling providers are correct/consistent for reference: MSSql (`IDENTITY`), PostgreSQL
  (`SERIAL`/`BIGSERIAL`, fixed under CR-M142) — no change expected, just cross-check.

## Acceptance criteria

- A dual-key model (`[PrimaryField] Guid` + non-PK `[IncrementField] int`) produces **valid** SQLite
  DDL and `CreateTable` succeeds (option a) — or throws a **descriptive** `NotSupportedException`
  (option b), per the chosen decision.
- An autoincrement **PK** column with additional `UNIQUE`/`NOT NULL` constraints produces valid DDL
  (`INTEGER PRIMARY KEY AUTOINCREMENT ...`) and a real insert auto-populates it.
- Regression tests in `Birko.Data.SQL.SqLite.Tests` (offline — the SQLite tier already runs here):
  the dual-key `CreateTable` case + the autoincrement-PK-with-constraints case.
- **Bonus:** if option (a) is taken, the CR-M166 SQL-sync `GetLastSyncTime`/`SetLastSyncTime` CRUD
  round-trips can then run on SQLite offline — **potentially closing CR-M166 without Docker** (verify
  and, if so, update STORY-028 cluster 1 + the audit).

## Resolution (2026-07-14, done)

Chose **option (a)** + the PK-adjacency/type fix. `SqLiteConnector.FieldDefinition` now:
- emits `INTEGER PRIMARY KEY AUTOINCREMENT` (adjacent, type forced to `INTEGER`) when a field is
  **both** primary and autoincrement, and
- emits a **plain column** (no `AUTOINCREMENT`) for a non-PK `[IncrementField]` — the caller assigns
  the value; documented inline that MSSql (`IDENTITY`) / PostgreSQL (`SERIAL`) support real non-PK
  identity and differ.

**Verified offline (no Docker):** `Birko.Data.SQL.SqLite.Tests` 24 green (no regression); the dual-key
`SqlSyncKnowledgeItem.CreateTable` now produces valid DDL and the full SQL sync-store CRUD round-trip
runs on a real SQLite `.db` (`Birko.Data.Sync.Sql.Tests` 2 → 6). **This closed CR-M166 offline** and
removed it from STORY-028 (integration-test tier).

## Notes

- **This is offline-fixable** — Microsoft.Data.Sqlite + the on-disk SQLite tier work in the current
  environment; it does **not** need the STORY-028 Docker harness. It is filed loose (like TASK-036)
  rather than under EPIC-014 because it is a newly-found framework bug, not a `CR-*` audit finding.
- Left as a task rather than fixed inline because of the option-(a)/(b)/(c) design decision above —
  worth a deliberate call (and possibly the user's) rather than a silent behaviour change.
