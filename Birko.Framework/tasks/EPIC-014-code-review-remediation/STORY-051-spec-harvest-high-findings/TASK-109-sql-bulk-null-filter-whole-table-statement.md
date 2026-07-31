---
id: TASK-109
parent: STORY-051
feature: null
status: todo
priority: P0
assignee: ai
created: 2026-07-30
depends-on: []
blocks: []
pr: null
github-issue: null
jira-key: null
findings: [SH-H002, SH-M023]
---

# A null or untranslatable filter renders `DELETE FROM "T"` — the whole table

## Context

`../Birko.Data.SQL/Stores/DataBaseBulkStore.cs:156` — **CONFIRMED**.

`Delete(filter)` forwards `filter as LambdaExpression` to `Connector.Delete` with **no null check and no
translatability check**. `AbstractConnector_Delete.cs:30` builds `"DELETE FROM " + QuoteIdentifier(name)`
and appends the `WHERE` **only when conditions exist**. So zero conditions is a whole-table delete.

Zero conditions happens two ways, and that is what makes this high rather than merely sloppy:
`DataBase.ParseConditionExpression`'s fall-through returns `Array.Empty<Condition>()` (`DataBase.cs:818`)
both for a **null filter** and for **every predicate shape it cannot parse** — e.g. the
`InvocationExpression` produced by `x => pred(x)`. A filter that is silently dropped is indistinguishable
from no filter at all.

Same shape at `DataBaseBulkStore.cs:129` (an `UPDATE` with no `WHERE`) and at
`AsyncDataBaseBulkStore.cs:174/211/213`.

**`SH-M023` is the portable-store twin and belongs here.** `AbstractBulkStore.cs:99` declares
`Delete(filter)`'s parameter non-nullable but never guards it; `Delete(null!)` materialises the entire table
and hands it to `DeleteCore`. Same at `AbstractAsyncBulkStore.cs:146`, and at `76`/`117` for
`Update(filter, Action)` / `UpdateAsync`. One decision covers both layers.

## The fix shape is already in this repo

`Birko.Data.ElasticSearch` solved exactly this under CR-H047 (2026-07-27) and its split is the precedent to
follow: **three outcomes stay distinct, and only one is an error.**

| Outcome | Meaning | Behaviour |
|---|---|---|
| No filter | read/act on everything, deliberately | allowed **only** where a null filter is a documented API (reads) |
| Matches nothing | a legitimate translation | render an always-false predicate — never omit the `WHERE` |
| Cannot be expressed | translator limitation | **throw** |

ES has two helpers for this — `ParseFilterQuery` (optional filter) and `ParseRequiredFilterQuery` (the
destructive paths, where a null filter throws). SQL needs the same seam so the destructive paths cannot
inherit the permissive one. Note the empty-`IN` work from the same date already established that an
untranslatable operand must not silently collapse: `1 = 0` for an empty `IN`, `1 = 1` for an empty
`NOT IN`.

## Acceptance criteria

- [ ] `ParseConditionExpression` distinguishes "no filter" from "could not translate" — the latter throws
      rather than returning an empty condition set
- [ ] SQL `Delete(filter)` / `Update(filter, PropertyUpdate)` / `Update(filter, Action)` **throw** on a null
      filter, sync and async, on all four providers
- [ ] An untranslatable-but-non-null predicate (e.g. `x => pred(x)` via `InvocationExpression`) throws on
      the destructive paths instead of deleting everything
- [ ] A predicate that legitimately matches nothing deletes/updates **zero** rows and still emits a `WHERE`
- [ ] `AbstractBulkStore` / `AbstractAsyncBulkStore` guard the null filter on `Delete` and both `Update`
      overloads (SH-M023), so the portable path fails the same way
- [ ] Reads are unchanged — a null filter on `Read(filter, …)` still means read-everything, which is a
      documented API
- [ ] Regression tests in `Birko.Data.SQL.Tests` (statement text) **and**
      `Birko.Data.SQL.SqLite.Tests` (end-to-end: rows survive), plus `Birko.Data.Stores` coverage for the
      portable guard
- [ ] `/specs regen` for `bulk-filter-operations` and `store-crud-contract`, spec diffs reviewed

## Out of scope

- Widening what the parser *can* translate. This task makes an untranslatable filter loud, it does not make
  more shapes translatable. `SH-H021`/`SH-H026` (an untranslatable operand read as constant TRUE, and an
  unhandled node removing the whole `WHERE`) are the same family in the *read* path and are still
  unverified — separate tasks under `filter-expression-translation`.
- `SH-M024` (an unrecognised method call in an `UPDATE SET` value reflectively invoked with null args).

## Human test plan

N/A — covered by automated tests. The SQLite-backed end-to-end assertions cover the real consequence
(rows still present after a rejected delete), which is the only part a human could otherwise check.
