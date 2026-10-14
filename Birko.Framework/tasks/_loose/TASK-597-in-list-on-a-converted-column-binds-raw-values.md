---
id: TASK-597
parent: null
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: done
picked-by: fix-next
# blocked: <reason> — add this line while the task is blocked, keeping its status; /tasks unblock removes it
priority: P3
assignee: ai
created: 2026-10-14
depends-on: []
blocks: []
# findings: ids this task remediates — from a review/audit/harvest/drill pass, or from ordinary
# field use with no pass behind it at all. Prefixes: see /tasks intake
findings: [FIELD-035]
pr: null
github-issue: null
jira-key: null
---

# An `IN` list (`values.Contains(x.Col)`) on a converted SQL column may still bind raw values

## Context

Spawned from [[TASK-596]]'s outcome, 2026-10-14. TASK-596 made a comparison's value (`x.At == instant`, `<`, `>`, …)
bind through the column field's `ToStoredValue`, in `DataBase.StoreComparedValues`. A `Contains` call that becomes
`IN (…)` (`instants.Contains(x.At)`) is built by a different branch of `ParseConditionExpression`, the method-call
branch, and was not touched. On a `[UtcField]` column, an `IN` list of `DateTime`s would then compare offset-less
text against stored offset text and match nothing, the same defect TASK-596 fixed for `==`. **Not measured.**

## Acceptance criteria

- [x] Measured on SQLite first (rule 54): `instants.Contains(x.At)` on a `[UtcField]` column, against entity-written
      rows. The finding is confirmed or falsified.
- [x] If confirmed: the `IN` values go through the field's `ToStoredValue`, the same single producer as TASK-596. An
      empty list keeps meaning "matches nothing".
- [x] A regression test fails with the fix removed.

## Out of scope

- Other converted kinds than `[UtcField]`. TASK-596 measured `TimeOnly`, enums, `decimal` and plain `DateTime` as
  storing their CLR value on SQLite, so an `IN` over them is unaffected there.

## Human test plan

N/A — fully covered by automated tests.

## Implementation plan

_Populated by `/tasks plan TASK-597` — leave empty until then._

## Outcome

**Fix.** An `IN` list now binds its values in the column's stored form, through the same helper TASK-596's
comparisons use (`DataBase.StoreColumnValues` → `AbstractField.ToStoredValue`). Measured before it, on SQLite with a
`[UtcField]` column: `instants.Contains(r.At)` matched **no** row, and `!instants.Contains(r.At)` excluded none.
- The column is found among the method's arguments (static `Enumerable` / `MemoryExtensions.Contains`) or its
  object (`List<T>.Contains`).
- `StoreComparedValues` now delegates to the shared `StoreColumnValues`, so comparisons and `IN` have one producer.

**Step-6 split** (`tests/Birko.Data.SQL.SqLite.Tests/PropertyUpdateStorageConversionTests.cs`, 502 tests):
- Removing the `IN` call fails 2 of 502: `AnInList_OnAUtcColumn_MatchesRowsWrittenEitherWay` and
  `ANotInList_OnAUtcColumn_ExcludesRowsWrittenEitherWay`.
- **Contract pin, not evidence:** `AnEmptyInList_StillMatchesNothing`. It passes either way, and guards that the
  empty-list rule was not disturbed.
- Every SQL test suite is green, including the 710 `Birko.Data.SQL.Tests` translator tests.

**Judgement calls.** An `IN` over an enum column now also passes through `IntegerField.ToStorage` (enum → int). That
changes nothing, because `AddParameter` already normalised enums, and the suites confirm it.

**Observable change.** As TASK-596: a `[UtcField]` value written by `PropertyUpdate` before TASK-596 is offset-less,
and still won't match an `IN` until the entity is re-saved.

## Progress log

- step 2 — picked; the only defect the user named (same root cause as TASK-596, the Contains/IN branch)
- step 3 — verified: held (IN matched 0 rows, NOT IN excluded none, on a [UtcField] column on SQLite)
- step 4 — layer: local (Birko.Data.SQL condition parser)
- step 5 — fix in DataBase.cs (StoreColumnValues, called from the Contains/IN branch); tests in PropertyUpdateStorageConversionTests.cs; 502/502, every SQL suite green
- step 6 — removed the IN call: 2/502 failed; fix-dependent = AnInList…, ANotInList…; contract pin = AnEmptyInList…
- step 7 — respecced filter-expression-translation (IN scenario added to the TASK-596 requirement)
- step 8 — handed to /tasks close --unattended; outcome in status:, commit in git log
