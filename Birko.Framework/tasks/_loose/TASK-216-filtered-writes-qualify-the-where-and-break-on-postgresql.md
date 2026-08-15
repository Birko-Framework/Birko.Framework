---
id: TASK-216
parent: null
feature: null
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: todo
priority: P1
assignee: ai
created: 2026-08-15
depends-on: []
blocks: []
related: [TASK-211, TASK-209, TASK-110]
pr: null
github-issue: null
jira-key: null
findings: []
---

# A filtered DELETE / UPDATE qualifies its `WHERE` with a bare table name, so every filtered write fails on PostgreSQL

## Context

Measured on real PostgreSQL 16.4 (2026-08-15) while closing [[TASK-211]], which fixed the identical
mechanism on the **read** path. Spawned rather than absorbed: it is a different builder, it needs a
different fix, and its failure mode is the opposite one.

`DataBase.ResolveColumnName(exprType, property, withTableName: true)` qualifies every condition name, so a
filtered write emits the qualifier bare while the statement's target table is quoted:

```sql
DELETE FROM "OfPersons" WHERE OfPersons.Name = $1
-- ERROR: missing FROM-clause entry for table "ofpersons" at character 31
```

On PostgreSQL — the one supported provider that case-folds an unquoted identifier — `OfPersons` folds to
`ofpersons` and does not match the quoted relation. Every `Delete(filter)` / `Update(filter, …)` on an
entity whose table name is not already all-lower-case fails.

**Unlike the read defect, this one is LOUD.** `missing FROM-clause entry for table` does not contain the
missing-relation wording, so `PostgreSQLConnector_OnException` rethrows rather than swallowing (and after
TASK-211's narrowing it rethrows for an even wider set). So this is a hard failure a consumer would notice,
not the silent-empty-result class TASK-211 was filed for. That is the whole reason it was left out: the
severity is genuinely lower, and the fix is not the same one.

**TASK-211's fix does not reach here, and its mechanism does not port.** That task emits the FROM/JOIN table
as `"T" AS T` — a quoted relation with a bare alias, so every bare qualifier folds onto the alias. The same
trick on a write is not portable:

| Provider | `DELETE FROM t AS a` |
|---|---|
| PostgreSQL | supported |
| SQLite | supported |
| MySQL | only since 8.0.16 |
| **MSSql** | **not supported** — requires `DELETE a FROM t AS a` |

So the write path needs its own decision, which is what this task owns.

## Approach

Two candidates; neither is obviously right, so cost both before choosing.

1. **Don't qualify a single-table statement.** A write always targets exactly one table, so the qualifier is
   never needed for disambiguation. `DataBase_OrderBy` already does exactly this
   (`withTableName = tableList.Length > 1`), so the precedent is in the codebase. Cheapest and most
   portable, but the condition parser is shared with reads and does not know which statement it is feeding —
   changing it globally would strip the qualifier from multi-table reads, which need it.
2. **Quote the qualifier at render time** in the write builders, where the target table name is known. Fixes
   it without touching the parser, and matches what `ViewSelectSqlBuilder.QuoteFieldReference` does for the
   DDL path. Note the trap TASK-211 recorded: a qualifier can arrive function-wrapped (`LOWER(T.Col)`,
   `COALESCE(…)`, the `.Date` rewrite), so a naive split-on-first-dot silently misses those — and a missed
   producer is the same failure, which is how this family keeps surviving.

Whichever is chosen, **verify against real PostgreSQL** — the recipe is in TASK-211's § Measured (EDB
portable binaries, no Docker, no admin).

## Acceptance criteria

- [ ] Reproduced on real PostgreSQL first, and the recipe recorded
- [ ] A filtered `Delete(filter)` and a filtered `Update(filter, …)` over a PascalCase model both work on
      PostgreSQL, including a filter whose column reference is function-wrapped (`LOWER`, `COALESCE`, `.Date`)
      — that shape is the one a partial fix misses
- [ ] The chosen mechanism is recorded in `CLAUDE.md` § Conventions next to TASK-211's alias rule, and says
      why the write path does not use the alias
- [ ] `SelectCount` and the aggregate path are checked on PostgreSQL too — they share the condition
      renderer, and this task should not leave a fourth sink for someone else to rediscover
- [ ] Red-verified; split as numbers, contract pins named as pins. **A SQLite-only assertion is a pin by
      construction** (SQLite is case-insensitive for identifiers), as TASK-209 recorded
- [ ] Full SQL suite sweep (TASK-211 swept 23 suites; the same set applies)

## Out of scope

- The read path — [[TASK-211]] closed it and this task must not regress it.
- A permanent PostgreSQL CI tier (STORY-042's Docker tier). This needs one reproduction, not a suite.

## Human test plan

N/A — a filtered write either removes/changes the right rows on a live PostgreSQL or it does not, which an
automated test observes directly.

## Implementation plan

_Populated by `/tasks plan TASK-216` — leave empty until then._
