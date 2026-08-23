---
id: TASK-275
parent: EPIC-014
feature: FEATURE-014
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: done
priority: P1
assignee: ai
created: 2026-08-22
depends-on: []
blocks: []
related: [TASK-257, TASK-265, TASK-273]
findings: []
pr: 0c720aa (Birko.Data.SQL) · b3f0ac1 (.SqLite) · 9a19da0 (.MySQL) · cfd41d7 (.PostgreSQL) · 3a2b287 (.MSSql)
github-issue: null
jira-key: null
---

# `[UniqueField]` on a nullable column is an inline constraint, so on MSSql it rejects the second ordinary row — and no predicate can be attached to it

## Context — found at TASK-273's plan review, measured on the live server

TASK-273 gives `[CompositeIndex]` / `[IndexedField]` a `WhereNotNull` / `WhereNull` predicate so a unique
index over a nullable column stops breaking inserts on SQL Server. **A third declaration surface has the
identical defect and that fix cannot reach it.**

`[UniqueField]` (and `[PrimaryField]`) do not produce an index definition at all — `FieldDefinition` emits
`UNIQUE` as an **inline column constraint** inside `CREATE TABLE`, on all four providers. TASK-257 recorded
this while typing MSSql's string columns; it is the same fact from the other side.

**Measured, 2026-08-22, live SQL Server 2022 (16.0.4265.3):**

| Probe | Result |
|---|---|
| `CREATE TABLE R2Probe (T UNIQUEIDENTIFIER NOT NULL, Code NVARCHAR(255) NULL UNIQUE)` | OK |
| first row with `Code = NULL` | OK |
| **second row with `Code = NULL`** | **`Msg 2627` — Violation of UNIQUE KEY constraint … duplicate key value is (`<NULL>`)** |
| `… Code NVARCHAR(255) NULL UNIQUE WHERE Code IS NOT NULL` | **`Msg 156` — Incorrect syntax near 'WHERE'** |

So `[UniqueField] public string? Code` admits **one** NULL row per table on MSSql and refuses every
subsequent row that leaves the column unset — the ordinary case, since the column is nullable precisely
because most rows have no value. PostgreSQL, SQLite and MySQL treat NULLs as distinct and admit any number
(TASK-273's M1a), so this is silent on the three providers consumers test on and fatal on the one they do not.

Note the error code differs from the index case: **2627** for a constraint, **2601** for a unique index
(TASK-273 M1a). A test copied from the sibling task asserting 2601 would pass for the wrong reason.

**Why TASK-273's fix cannot be extended to cover it.** A predicate is syntactically impossible on an inline
column constraint (`Msg 156`). The only shapes that work are:

1. stop emitting the inline `UNIQUE` for a **nullable** column and emit a separate filtered/partial
   `CREATE UNIQUE INDEX … WHERE <col> IS NOT NULL` instead — correct on MSSql, PostgreSQL and SQLite, and on
   MySQL an unfiltered unique index is already equivalent (NULLs distinct);
2. keep the constraint and document that `[UniqueField]` is only for always-populated columns — which is
   what the attribute remarks effectively claim today and what consumers plainly do not read that way.

(1) changes `CREATE TABLE` output for every nullable `[UniqueField]` column on every provider, which is why
this is a separate task from TASK-273 rather than one more site in it.

⚠ **`IsInIndexKey` already couples these two surfaces, so a change here is not local.** TASK-257 made MSSql
bound an unlengthed string to `NVARCHAR(255)` when it is an index key, and `IsInIndexKey` is
`IsIndexed || IsUnique || IsPrimary` **precisely because** `UNIQUE` / `PRIMARY KEY` arrive as inline column
constraints. Moving a nullable `[UniqueField]` column out of the constraint and into an index changes which
branch of that predicate it takes — and the bounded branch is what makes the write refuse rather than
silently truncate (TASK-257's `ANSI_WARNINGS` note). Re-measure both, do not reason.

## Acceptance criteria

- [x] Blast radius measured first: how many `[UniqueField]` / `[PrimaryField]` declarations across the
      framework, its tests and all 16 consumer repos sit on a **nullable** column. Attributes are often
      declared fully qualified in consumer code, so verify the sweep against one known instance before
      trusting the count (TASK-248's survey had to be corrected twice for exactly this).
- [x] A decision recorded here between shape (1) and shape (2) above, with the measured counts behind it.
- [x] Shape (1): the emitted `CREATE TABLE` for a nullable `[UniqueField]` column no longer carries inline
      `UNIQUE`, and the equivalent unique index is created instead — on all four providers, with the MySQL
      case emitting no predicate (unfiltered is already equivalent there).
- [x] `IsInIndexKey` re-measured on MSSql for the moved column: it must still take the bounded branch, or an
      unlengthed nullable `[UniqueField]` string becomes `NVARCHAR(MAX)` and its index becomes unbuildable
      (`Msg 1919`) — swapping one silent defect for another.
- [x] Live per provider, **both directions**: two rows with the column NULL are accepted, two rows sharing a
      non-NULL value are rejected. Assert the provider's code (2601 for an index, 2627 for a constraint).
- [x] Nothing changes for a **non**-nullable `[UniqueField]` column — its DDL must be byte-identical.
- [x] Mutation-proven: three mutations, all provider-correct — see below.
- [x] ⚠ An existing database is **not** repaired by any of this — schema-ensure never alters an existing
      table (TASK-257) and never reconciles an existing index (TASK-273 R3b). State the hand-run remedy, as
      TASK-257 did for its `TEXT` columns.

## Out of scope

- `[CompositeIndex]` / `[IndexedField]` predicates — TASK-273 owns them, and this task should land after it
  so the index-emitting machinery already exists.
- Reporting declared-vs-stored schema drift — TASK-269's family.
- The second index lane (`IIndexManager` / migrations) — TASK-274.

## Human test plan

- [ ] N/A — four live-server behavioural suites plus a mutation; nothing a person drives.

## Implementation plan

_Populated by `/tasks plan TASK-275` — leave empty until then._

---

## Closed 2026-08-23

**Shape (1): a nullable `[UniqueField]` column drops the inline constraint and gains
`ux_{table}_{column}`, a UNIQUE index filtered to its non-null rows.** Scoped to nullable columns —
`[RequiredField]` and `[PrimaryField]` keep the inline form with byte-identical DDL.

### Why (1) rather than (2), on the measurement

**2** production `[UniqueField]` declarations sit on a nullable column across the framework, its tests and
all consumer repos: BardStudio `MusicTrack.FilePath` and Presenter `PresentationEntity.Slug`. Worth noting
*why* they count — both are **non-nullable in C#**, and this framework does not read nullable-reference
annotations, so they are nullable in the database. Every `[PrimaryField]` hit is the framework's own `Guid?`
key, where `PRIMARY KEY` implies NOT NULL and the question cannot arise.

Two declarations is small enough to change the shape rather than document a limit, and (2) was rejected
precisely because the current remarks already imply that limit and consumers plainly do not read them that
way. Refusing the declaration outright — the § SH-H037 instinct — was rejected for TASK-248's reason: it
would break two entities that work correctly today on the providers they actually run on.

### What was built

- `AbstractField.UsesInlineUniqueConstraint` = `IsUnique && (IsNotNull || IsPrimary)` — one producer, and all
  four `FieldDefinition`s consult it instead of `IsUnique`.
- `DataBase.LoadIndexes` synthesises the index when that is false, **routed through TASK-273's
  `WhereNotNull` accumulator** so it inherits that task's rendering, validation, refusal and per-provider
  policy rather than getting a second implementation. On MySQL the term is dropped there (NULLs already
  distinct), so the emitted index means the same thing.
- A name collision with a declared index **throws** rather than merging into it.

### DDL changes on four providers; behaviour changes on one

That is the claim each per-provider suite makes, and it is the reason they all exist: SQLite, PostgreSQL and
MySQL assert the rule is **unchanged**, MSSql asserts it is **fixed**. The error code moves with the shape,
2627 (constraint) → 2601 (index), exactly as this file predicted before the work started.

### Two coupled facts, re-measured rather than assumed

- The column becomes `IsIndexed`, which bounds it on MySQL — and that is what makes the synthesised index
  buildable there at all, since `LONGTEXT` cannot be indexed (ERROR 1170). **⚠ This incidentally covers part
  of TASK-265**: an unlengthed nullable `[UniqueField]` string previously could not have its table created
  on MySQL. Recorded as an overlap, not a closure — TASK-265 still owns the `[PrimaryField]` and
  non-nullable cases, which keep the inline form.
- `IsInIndexKey` is unchanged (still true via `IsUnique`), so TASK-257's MSSql bounding still applies — and
  the synthesised index **requires** it, because `NVARCHAR(MAX)` cannot be an index key (`Msg 1919`). Pinned
  by a live test that would otherwise show up as a recorded index failure and a silently absent constraint.

### Two of TASK-257's pins changed meaning, and were updated rather than deleted

Its claim — *a `UNIQUE`/`PRIMARY` column is an index key that `IsIndexed` cannot see* — is now true only of
the shapes that keep the inline constraint. `IsInIndexKeyTests` and MSSql's `IndexedStringColumnTypeTests`
moved their probe columns to `[RequiredField]`, preserving exactly what they were about, and a new test
asserts the other side: a nullable unique column **is** visible to `IsIndexed`, because it now has a real
index. The pair is the record of where the line moved.

### Verification

**1,430 tests, 0 failed, 0 skipped** across eighteen suites with `BIRKO_REQUIRE_LIVE` set throughout (live
SQL Server 2022, PostgreSQL 16.15, MySQL 8.4.11, on-disk SQLite) — **11 new**. The sweep deliberately spans
every SQL-touching suite, including `BackgroundJobs.SQL`, `Workflow.SQL`, `EventBus.Outbox.SQL`,
`Data.Sync.Sql` and the four `Models.*.SQL` domains, because this changes `CREATE TABLE` output for any
entity with a nullable unique column.

| Mutation | MSSql | SQLite | MySQL |
|---|---|---|---|
| inline `UNIQUE` for every unique column again (the defect) | 2 red | 2 red | 2 red |
| no synthesised index (constraint dropped entirely) | 2 red | 2 red | 2 red |
| synthesised index without its `WhereNotNull` predicate | 2 red | 1 red | **green** |

The third row is the provider-correct signature: MySQL drops the term anyway, so removing it is invisible
there — which is what makes the first two rows meaningful rather than coincidental.

### Deliberately not done

- **No repair of an existing database.** A table created before this keeps its inline constraint and keeps
  rejecting the second NULL row on SQL Server. The hand remedy (drop the constraint, create the filtered
  index) is recorded in `Birko.Data.SQL/CLAUDE.md`. Auto-`ALTER` on schema-ensure is the quiet destructive
  write § Conventions forbids.
- **TASK-265 is not closed** — only the nullable-unique shape it shares with this task now works on MySQL.
- **`[PrimaryField]` untouched.** `PRIMARY KEY` implies NOT NULL, so the NULLs-are-equal question cannot
  arise, and moving it would change the table's key rather than an auxiliary constraint.
