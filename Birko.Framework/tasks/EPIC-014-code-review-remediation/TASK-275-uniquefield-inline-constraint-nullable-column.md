---
id: TASK-275
parent: EPIC-014
feature: FEATURE-014
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: todo
priority: P1
assignee: ai
created: 2026-08-22
depends-on: []
blocks: []
related: [TASK-257, TASK-265, TASK-273]
findings: []
pr: null
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

- [ ] Blast radius measured first: how many `[UniqueField]` / `[PrimaryField]` declarations across the
      framework, its tests and all 16 consumer repos sit on a **nullable** column. Attributes are often
      declared fully qualified in consumer code, so verify the sweep against one known instance before
      trusting the count (TASK-248's survey had to be corrected twice for exactly this).
- [ ] A decision recorded here between shape (1) and shape (2) above, with the measured counts behind it.
- [ ] If (1): the emitted `CREATE TABLE` for a nullable `[UniqueField]` column no longer carries inline
      `UNIQUE`, and the equivalent unique index is created instead — on all four providers, with the MySQL
      case emitting no predicate (unfiltered is already equivalent there).
- [ ] `IsInIndexKey` re-measured on MSSql for the moved column: it must still take the bounded branch, or an
      unlengthed nullable `[UniqueField]` string becomes `NVARCHAR(MAX)` and its index becomes unbuildable
      (`Msg 1919`) — swapping one silent defect for another.
- [ ] Live per provider, **both directions**: two rows with the column NULL are accepted, two rows sharing a
      non-NULL value are rejected. Assert the provider's code (2601 for an index, 2627 for a constraint).
- [ ] Nothing changes for a **non**-nullable `[UniqueField]` column — its DDL must be byte-identical.
- [ ] Mutation-proven: revert the emitter change and the MSSql two-NULL-rows test goes red.
- [ ] ⚠ An existing database is **not** repaired by any of this — schema-ensure never alters an existing
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
