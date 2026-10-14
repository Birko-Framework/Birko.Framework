---
id: TASK-596
parent: null
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: done
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

### Re-verified 2026-10-14 (fix-next step 3): real, and differently scoped

Measured on SQLite, the framework's default provider, in `tests/Birko.Data.SQL.SqLite.Tests/PropertyUpdateStorageConversionTests.cs`.
The same values were written once by an entity create and once by `UpdateAsync(filter, PropertyUpdate.Set(...))`:

| Field | Entity write stores | PropertyUpdate.Set stored | Effect |
|---|---|---|---|
| `[UtcField] DateTime` | `2026-10-14 09:30:15.123+00:00` (TEXT) | `2026-10-14 09:30:15.123` (TEXT) | **an equality filter on the column misses the updated row**; reads look fine |
| `TimeOnly` | same | same | none: the driver's format equals the field's |
| enum | same | same | none: `AddParameter` normalises enums on every path |
| `decimal` | same | same | none: no `ToStorage` for decimal; both paths bind the raw value |
| plain `DateTime` | same | same | none |

So it is **worse** than filed (on SQLite, not only on PostgreSQL), and **narrower** (`[UtcField]` is the measured
casualty). The root cause is general: the translator skips `ToStorage`. So the fix covers every field that overrides
it, not only `[UtcField]`.

## Acceptance criteria

- [x] Measured first (rule 54) — done at step 3, table above: `[UtcField]` confirmed on SQLite; `TimeOnly`, enum,
      `decimal`, plain `DateTime` falsified on SQLite.
- [x] A filtered `PropertyUpdate.Set` binds its value through the field's own `ToStorage`, with one producer for both
      paths and no second conversion in the translator. That holds for sync (`DataBaseBulkStore`) and async
      (`AsyncDataBaseBulkStore`) alike, since both use `PropertyUpdateSqlTranslator`.
- [x] On SQLite, every column written by a Set stores exactly what an entity write stores: storage class and value.
      An equality filter on a `[UtcField]` column finds rows written either way.
- [x] Regression tests fail with the fix removed.
- [x] _Added at step 5, because the same root cause sat in the filter path (findings travel in packs): a value
      compared with a column in `ParseConditionExpression` is bound through the field's `ToStoredValue` too.
      Before this fix an equality filter on a `[UtcField]` column matched **no row an entity had written**, on SQLite._

## Out of scope

- `Increment` / `Decrement`: they go through `IncrementExpression`, a separate producer, and TASK-513 covers it for
  decimals.

## Human test plan

N/A — fully covered by automated tests. What is stored is read from the database.

## Implementation plan

_Populated by `/tasks plan TASK-596` — leave empty until then._

## Outcome

**Fix.** Every value a SQL query binds for a column now goes through that column's own storage conversion
(`AbstractField.ToStorage`, reached through the new public `ToStoredValue`). Before, a filtered `PropertyUpdate` Set
and a filter's compared value were bound raw. For a `[UtcField]` `DateTime` that meant offset-less text
(`2026-10-14 09:30:15.123`) beside entity-written offset text (`…+00:00`). The two sides then never compared equal, so
an equality or range filter on a UTC column missed every normally-written row, silently, on the default provider.
- `Birko.Data.SQL/Stores/PropertyUpdateSqlTranslator.cs`: a Set binds `field.ToStoredValue(set.Value)`. This covers
  sync and async, which share it.
- `Birko.Data.SQL/SQL/DataBase.cs`: `StoreComparedValues` runs after `ParseConditionExpression` builds a column-vs-value
  comparison. It converts the values whichever side the column is on, and skips column-to-column comparisons and
  `.Date` comparisons.
- `Birko.Data.SQL/SQL/Fields/AbstractField.cs`: `ToStoredValue(raw) => ToStorage(raw)`, the one producer.

**Step-6 split** (`tests/Birko.Data.SQL.SqLite.Tests/PropertyUpdateStorageConversionTests.cs`, 499 tests in the suite):
- Reverting the PropertyUpdate half fails 4 of 499: `APropertyUpdateSet_StoresWhatAnEntityWriteStores(At)`,
  `AFilterOnAUtcColumn_MatchesARowWrittenEitherWay`, `AUtcFilter_Matches_WithTheConstantOnEitherSide_AndInARange`,
  `TheSyncBulkStore_StoresAndFiltersTheSameWay`.
- Reverting the filter half fails 3 of 499: the last three of those.
- **Contract pins, not evidence:** `StoresWhatAnEntityWriteStores` for `Opens` / `State` / `Amount` / `Plain`, and
  `APropertyUpdateSet_ReadsBackLikeAnEntityWrite`. They pass with or without the fix, because those fields were never
  affected and reading is lenient.
- All 26 SQL test suites are green, including `Birko.Data.SQL.Tests` (710), which covers the translator.

**Judgement calls.**
- **Widened to the filter path** rather than spawning it: one root cause, one line each, and fixing only the write
  side would have flipped which rows a UTC filter misses instead of fixing it.
- **The conversion is applied at the condition, not in `AddParameter`.** The connector sees only a parameter name and
  a value, so it cannot know the column's field. Moving the conversion there would need the field threaded through
  every provider.
- **A stricter option was rejected:** refusing a `PropertyUpdate` on a converted field. It would have broken a
  documented API that is right once the conversion runs.

**Observable change (migration).** Rows whose `[UtcField]` column was written by `PropertyUpdate.Set` **before** this
fix hold offset-less text. They read back correctly, but equality and range filters no longer match them (before the
fix they matched instead of the normal rows). Re-save the entity, or rewrite the column with an entity update, to
repair them. `TimeOnly`, enums, `decimal` and plain `DateTime` were measured unaffected on SQLite.

**Flagged, not fixed.** `Contains` / `IN` lists on a converted column are not converted. They go through a different
branch, and no case is measured → [[TASK-597]] (FIELD-035). Other providers (PostgreSQL `[UtcField]`, MSSql, MySQL) are not live-measured
here; their offline suites are green.

## Progress log

- step 2 — picked; ranked above TASK-595 because silent data corruption through the documented native write path (key 1, key 3) outranks a missing index / unique constraint
- step 3 — verified: rescoped — confirmed for [UtcField] on SQLite (offset dropped; an equality filter misses the row), falsified for TimeOnly / enum / decimal / plain DateTime on SQLite; Context + criteria corrected before code
- step 4 — layer: local (Birko.Data.SQL owns PropertyUpdateSqlTranslator and AbstractField)
- step 5 — fix in PropertyUpdateSqlTranslator.cs, DataBase.cs (StoreComparedValues), AbstractField.ToStoredValue; tests in tests/Birko.Data.SQL.SqLite.Tests/PropertyUpdateStorageConversionTests.cs; SQLite suite 499/499, all 26 SQL suites green
- step 6 — reverted PropertyUpdate half: 4/499 failed; reverted filter half: 3/499 failed; fix-dependent = StoresWhat(At), FilterMatches, UtcFilter either side + range, SyncBulkStore; contract pins = StoresWhat(Opens/State/Amount/Plain), ReadsBack
- step 7 — respecced bulk-filter-operations + filter-expression-translation by hand (two added requirements, no existing one contradicted)
- step 8 — handed to /tasks close --unattended; outcome in status:, commit in git log
