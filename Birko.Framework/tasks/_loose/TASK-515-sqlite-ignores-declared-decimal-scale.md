---
id: TASK-515
parent: null
feature: null
status: todo
priority: P3
assignee: ai
created: 2026-10-04
depends-on: [TASK-513]
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# SQLite accepts a declared decimal precision / scale and does nothing with it

## Context

Found by the TASK-513 close gate (conventions pass, rule 55: *"a builder … must honour a declaration or refuse it,
never accept one and do nothing"*, and rule 29: *"the opt-in promises only what the weakest provider can keep"*).

`[PrecisionField]` / `[ScaleField]` (or `HasPrecision(..).HasScale(..)`) on a `decimal` reach the column on MySQL,
SQL Server and PostgreSQL — `DECIMAL(22,6)` rounds `1.2345678` to `1.234568`, and a value past the precision is
refused. On SQLite they never did anything: before TASK-513 they rendered `NUMERIC(p,s)`, whose precision and scale
SQLite ignores (measured: stored as a float); since TASK-513 every decimal is `TEXT COLLATE BIRKO_DECIMAL` and they
are not rendered at all. So the same model stores `1.2345678` on SQLite and `1.234568` elsewhere, and nothing says so
except `Birko.Data.Stores/README.md`'s caveat.

Not a regression of TASK-513 — the gap is as old as the SQLite provider — but TASK-513 made the decimal path exact,
which makes the remaining difference the only one left.

## Acceptance criteria

- [ ] Decide, with the owner: round to the declared scale on write (matching MySQL/SQL Server), refuse a value past
      the declaration, or keep the gap documented — on measurements of what each other provider does with an
      over-scale and an over-precision value
- [ ] If enforced: one producer for the rule, applied on every write path (insert, update, `PropertyUpdate` Set and
      Increment), with a test per path that fails without it
- [ ] If kept: the declaration's own documentation says SQLite does not enforce it

## Out of scope

- The storage type itself — TASK-513
- View aggregates — TASK-514

## Human test plan

N/A — fully covered by automated tests.

## Implementation plan

_Populated by `/tasks plan TASK-515` — leave empty until then._
