---
id: TASK-204
parent: null
feature: null
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: review
priority: P0
assignee: ai
created: 2026-08-12
completed: null
depends-on: []
blocks: []
findings: []
pr: 83651e0 (Birko.Data.SQL), cbaca07 (Birko.Data.SQL.SqLite.Tests)
github-issue: null
jira-key: null
---

# An index that could not be built took the entity's whole read surface with it — permanently

## Context

`../../Birko.Data.SQL/SQL/Connectors/AbstractConnector_Create.cs` (+ the async mirror and
`AbstractConnector.cs`).

Schema-ensure runs **lazily on first data access**: `AbstractAsyncStore.EnsureInitializedAsync` →
`InitCoreAsync` → `Connector.CreateTable(new[] { typeof(T) })`, and `_initialized = true` is set only
*after* `InitCoreAsync` returns (`Birko.Data.Stores/AbstractAsyncStore.cs:38-44`). So an exception out of
`CREATE INDEX` meant the store **never initialised**, and every subsequent operation re-entered
schema-ensure and re-threw — including reads that never touched the indexed column. It could not
self-heal, because the rows needed to repair it were unreachable through the store that refused to
initialise.

Measured in consumer **Symbio** (its `TASK-354`): one duplicate `(TenantGuid, OrderNumber)` pair left
behind by pre-allocator numbering made a later-declared UNIQUE index unbuildable.
`GET /api/manufacturing/orders`, the same route with a status filter, and the detail route all returned
**500**, while the sibling entity in the same module was fine. The same annotation sits on five further
entities there, so the blast radius was six entities' read surfaces.

Reproduced in the framework: a plain `ReadAsync` throws
`SQLite Error 19: 'UNIQUE constraint failed: BadIdxDocs.TenantGuid, BadIdxDocs.Number'` straight out of
schema-ensure.

**Provenance note:** the first version of this fix was found sitting **uncommitted** in
`Birko.Data.SQL`'s working tree with no tests and no task — the third instance of that pattern in a week,
after [[TASK-197]] and [[TASK-198]]. It is committed here with coverage, and with one defect of its own
fixed (see below).

## Approach

Schema-ensure now attempts **one index per statement** and **records** a failure instead of throwing:

- `IndexCreationFailure` (table, index name, error) + `AbstractConnector.IndexCreationFailures` and the
  `OnIndexCreationFailed` event, so a host can surface the condition at startup.
- The **public `CreateIndexes` / `CreateIndexesAsync` are unchanged and still throw**. An explicit call
  (e.g. `Birko.Data.Migrations.SQL`'s `SqlSchemaBuilder.cs:330`) is a caller asking for *this index now*
  and must fail loudly. Only schema-ensure degrades.
- One index per attempt, so a failure cannot hide the indexes declared behind it.
- Cancellation is rethrown in the async path rather than recorded as an index failure.

An index is a constraint/optimisation, so degrading it to "absent and reported" is strictly better than
"table unusable": the data stays reachable — which is also what makes repair possible at all — and the
condition is reported rather than swallowed.

### The defect found in the fix itself

The first version kept an **append-only `List`**. Connectors are cached process-wide per
(connector type, settings id) in `DataBase.GetConnector` (`SQL/DataBase.cs:56-66`), while `_initialized`
lives on the **store** — so a web app resolving a *scoped store per request* re-runs schema-ensure per
request against one shared connector. Measured: 5 per-request stores produced **5 recorded failures and 5
re-executed failing `CREATE UNIQUE INDEX` statements**. On a process-lifetime object that is unbounded
growth, and `OnIndexCreationFailed` fired on every HTTP request.

Fixed by making the report **current state, not history**:

- keyed by `(table, index)`, so an index appears at most once however many times schema-ensure ran;
- the event fires on the **transition** into failure, not per attempt;
- a successful build **clears** the record, so a repaired condition stops being reported.

The **re-attempt itself is deliberately kept** — it is what lets the index appear on its own once an
operator repairs the offending rows, with no restart. Only the bookkeeping is deduplicated. A future
reader may be tempted to skip known-failed indexes for the DDL cost; that would trade away self-heal.

## Acceptance criteria

- [x] An unbuildable index leaves reads, writes and `Count` working on that entity.
- [x] The failure is recorded with its table and index name, and raises `OnIndexCreationFailed`.
- [x] A buildable index declared after a failing one is still created.
- [x] Explicit `CreateIndexes` still throws (migrations must not degrade).
- [x] Per-request store instances do not accumulate duplicate reports or repeat the event.
- [x] Repairing the data clears the report and the index self-heals on the next schema-ensure.
- [x] The async schema-ensure overload degrades identically.
- [ ] Full `Birko.Data.SQL` + `Birko.Data.SQL.SqLite` suites green — **blocked, see below**.

## Verification

Regression suite: `Framework.Tests/Birko.Data.SQL.SqLite.Tests/UnbuildableIndexEndToEndTests.cs`
(9 tests), driving real stores against a real SQLite file — not hand-built connector objects.

**Red-verified** against the reverted fix: `UnbuildableUniqueIndex_LeavesTheReadSurfaceWorking` and
`AFailingIndex_DoesNotHideTheIndexesBehindIt` both fail with the genuine
`SQLite Error 19: 'UNIQUE constraint failed'`. The other three of the original five do not compile
pre-fix (they reference the new API) and are therefore **pins, not evidence** — recorded as such rather
than counted as a red-verified failure.

Pre-dedupe state at that point: `Birko.Data.SQL.SqLite.Tests` 131/131 and `Birko.Data.SQL.Tests` 417/417
green with the fix applied.

### Blocked: no .NET 10 SDK on this machine

The four **dedupe/recovery/async tests added for the second defect have not been run.** Mid-session an
`msiexec` (started 09:19:32, 2026-08-12) removed .NET 10: `C:\Program Files\dotnet\host\fxr` now holds
only 6.0.16 / 6.0.36 / 8.0.30, `shared/Microsoft.NETCore.App` likewise, and `sdk/10.0.{102,103,200,301}`
are empty shells of 0–1 items. Everything here targets `net10.0`, so no build or test can run.

**Next step:** reinstall the .NET 10 SDK, then run both suites and flip the last criterion. Until then
the dedupe half is reviewed but unverified, and this task stays `review`, not `done`.
