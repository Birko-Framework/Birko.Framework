---
id: TASK-329
parent: EPIC-014
feature: FEATURE-014
status: todo
priority: P0
assignee: ai
created: 2026-09-09
depends-on: []
blocks: []
related: [TASK-137, TASK-212, TASK-215, TASK-310]
findings: [SH-H002]
pr: null
github-issue: null
affects: [Birko.Data.SQL]
---

# The SQL bulk stores never apply `RequireBoundedFilter`, so a filter that reduces to every row rewrites the table

## Measured, not read — 2026-09-09

Found while reading `AsyncDataBaseBulkStore` for [[TASK-310]], then **measured** with a throwaway probe
against on-disk SQLite before filing:

```
Update(x => !empty.Contains(x.Name), r => r.Name = "OVERWRITTEN")
PROBE RESULT: thrown=NONE | overwritten=3 of 3 | untouched=0
```

Every row rewritten, **no exception**. This is [[TASK-137]]'s empty-`NOT IN` shape — a filter that is
non-null and constrains nothing — reaching a destructive path on the framework's **primary** provider.

## Why the existing guards all miss it

Three guards exist and none covers this path:

- **`RequireFilter`** — the SQL bulk stores' own `private static` guard. It refuses a **null** filter
  only, and this filter is not null.
- **`RequireBoundedFilter`** — the `PredicateScope`-backed guard [[TASK-215]] added for exactly this
  shape. Measured: it is declared **only** in `Birko.Data.Stores/AbstractBulkStore.cs` and
  `AbstractAsyncBulkStore.cs`, and `grep -rn RequireBoundedFilter Birko.Data.SQL/` returns **nothing**.
  The SQL bulk stores never call it.
- **The connector's `WouldTargetEveryRow` / `AddRequiredWhere`** (SH-H002) — these guard a *destructive
  statement*. This path emits none: it issues a `SELECT` (where an always-true predicate is perfectly
  legitimate) and then a per-row `UPDATE … WHERE Guid = @g`, each individually bounded. So nothing
  anywhere sees a whole-table write.

⚠ **The root cause is a hierarchy assumption that does not hold.** TASK-215 wired the guard into "the
base's own six filter-based destructive wrappers" on `AbstractBulkStore` / `AbstractAsyncBulkStore` —
but `AsyncDataBaseBulkStore<DB, T> : AsyncDataBaseStore<DB, T>, IAsyncBulkStore<T>` does **not** derive
from those. It implements the interface directly and carries its own copies of the filter-based
overloads, which is also why it has its own private `RequireFilter`. So the SQL layer was never covered
by that fix, and the two code comments on these overloads (*"read-then-loop, so no conditionless SQL is
emitted and the connector guard cannot see this path — but a null filter still means Read(null) = every
row"*) show the author reasoning about **null** and not about *reduces-to-everything*.

## Scope to establish before fixing

The probe covered the **async SQLite** `UpdateAsync(filter, Action<T>)`. The same shape is very likely
present on:

- `DataBaseBulkStore.Update(filter, Action<T>)` — the sync twin, same `RequireFilter`-only guard
  (read at `DataBaseBulkStore.cs:103`, **not** measured);
- the `PropertyUpdate<T>` and `Delete(filter)` overloads — these *do* reach the connector, so
  SH-H002's `AddRequiredWhere` pre-check may already cover them. **Measure; do not assume either way.**
- every other SQL provider, since the defect is in the shared base rather than in SQLite.

⚠ **Measure each overload separately.** § Conventions records this family's repeated lesson that a guard
present on one overload and absent on its neighbour ships as a store whose `Delete` refuses beside an
`Update` that rewrites every row.

## Acceptance criteria

- [ ] The scope is **measured** per overload and per direction (sync/async), not inferred from the async
      SQLite probe above — including which of them the connector guard already covers
- [ ] The guard is applied where it is missing, at the layer that cannot be bypassed. ⚠ Prefer wiring
      the existing `RequireBoundedFilter` producer over writing a second copy in the SQL layer; a rule
      with one statement and two implementations is the shape this epic keeps paying for
- [ ] `x => !empty.Contains(x.Col)` on every affected overload throws `WholeTableWriteException`, and
      the refusal **names the door this caller has** (`UpdateAllAsync(updates)` / `DeleteAllAsync()` for
      the async store, `UpdateAll` / `DeleteAll` for the sync one) — per § SH-H037/TASK-215
- [ ] `x => true` still works as the explicit all-rows synonym, and an ordinary bounded filter still
      updates only its own rows — both asserted, or the fix is indistinguishable from a blanket refusal
- [ ] Assertions are **counted rows**, never the absence of an exception — the probe above is the shape
      to copy
- [ ] Proven able to fail
- [ ] `/specs regen bulk-filter-operations` (and `store-crud-contract` if that area's globs reach the
      changed files), with the spec diff reviewed

## Out of scope

- The caching findings — [[TASK-310]] closed those; this was found while reading for it and is
  unrelated to caching.
- `PredicateScope` itself, which is correct and already used by the portable backends.

## Human test plan

- [ ] N/A — mechanical; the proof is the row count after a reduces-to-everything filter.
