---
id: TASK-596
parent: null
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: in-progress
picked-by: fix-next
# blocked: <reason> — add this line while the task is blocked, keeping its status; /tasks unblock removes it
priority: P2
assignee: ai
created: 2026-10-14
depends-on: []
blocks: []
# findings: ids this task remediates — from a review/audit/harvest/drill pass, or from ordinary
# field use with no pass behind it at all. Prefixes: see /tasks intake
findings: [FIELD-034]
pr: null
github-issue: null
jira-key: null
---

# A filtered `PropertyUpdate.Set` on SQL binds the raw value, skipping the field's storage conversion

## Context

Found at TASK-590's close review, confirmed by reading the code at TASK-593 (2026-10-14).
`Birko.Data.SQL/Stores/PropertyUpdateSqlTranslator.cs` turns each assignment into `col = @p`, and for a **Set** binds
`set.Value` **as it is**. An entity write goes through the field's own write path (`AbstractField.Write` /
`ToStorage`); an **Increment** goes through a connector hook (`IncrementExpression`, TASK-513). So a converted field
written by `UpdateAsync(filter, PropertyUpdate)` can be stored differently from the same value written by
`CreateAsync` / `UpdateAsync(entity)`.

Fields whose storage form differs from the CLR value, all candidates for this, none measured yet:
- **`[UtcField]` `DateTime` on PostgreSQL**: the reviewer's case. `Kind` is stripped and the value lands in
  `timestamptz` shifted by the session time zone.
- **`decimal` on SQLite**: since TASK-513 it is `TEXT COLLATE BIRKO_DECIMAL` with an exact text form. A raw decimal
  parameter may store a different text or a REAL, which then sorts or compares wrongly.
- Any other `ToStorage` override (enums, JSON-serialised fields, Guids by provider).

**Measured where it was cheap:** on SQLite, plain `DateTime?` / `Guid?` / `string` values written this way read back
identical, ticks and `Kind` included (`tests/Birko.Security.Grants.SQL.Tests/PropertyUpdatePathTests.cs`). That rules
out the plain types on SQLite, nothing else.

`PropertyUpdate` is the documented native path (CLAUDE.md § Conventions: "Use `PropertyUpdate<T>` for native
platform operations"), so every consumer using it on a converted field is exposed.

## Acceptance criteria

- [ ] Measured first (rule 54): for each converted field kind above, a value written by `UpdateAsync(filter,
      PropertyUpdate.Set(...))` is compared with the same value written by an entity update. Read back through the
      store **and** inspected as stored (`typeof()` / raw text on SQLite; the PostgreSQL live suite for `[UtcField]`).
      The findings are confirmed or falsified per kind.
- [ ] Where they differ: a Set binds through the same conversion an entity write uses, with one producer, the field's
      `ToStorage`, not a second copy in the translator.
- [ ] Regression tests per confirmed kind fail with the fix removed.

## Out of scope

- `Increment` / `Decrement`: they go through `IncrementExpression`, a separate producer, and TASK-513 covers it for
  decimals.

## Human test plan

N/A — fully covered by automated tests. What is stored is read from the database.

## Implementation plan

_Populated by `/tasks plan TASK-596` — leave empty until then._

## Progress log

- step 2 — picked; ranked above TASK-595 because silent data corruption through the documented native write path (key 1, key 3) outranks a missing index / unique constraint
