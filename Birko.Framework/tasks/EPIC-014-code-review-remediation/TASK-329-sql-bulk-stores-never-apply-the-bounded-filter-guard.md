---
id: TASK-329
parent: EPIC-014
feature: FEATURE-014
status: done
priority: P0
assignee: ai
picked-by: fix-next
created: 2026-09-09
depends-on: []
blocks: []
related: [TASK-137, TASK-212, TASK-215, TASK-310, TASK-330]
findings: [SH-H002]
pr: adedea7
github-issue: null
affects: [Birko.Data.SQL, Birko.Data.Stores, Birko.Data.Core]
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

- [x] The scope is **measured** per overload and per direction (sync/async), not inferred from the async
      SQLite probe above — including which of them the connector guard already covers
- [x] The guard is applied where it is missing, at the layer that cannot be bypassed. ⚠ Prefer wiring
      the existing `RequireBoundedFilter` producer over writing a second copy in the SQL layer; a rule
      with one statement and two implementations is the shape this epic keeps paying for
- [x] `x => !empty.Contains(x.Col)` on every affected overload throws `WholeTableWriteException`, and
      the refusal **names the door this caller has** (`UpdateAllAsync(updates)` / `DeleteAllAsync()` for
      the async store, `UpdateAll` / `DeleteAll` for the sync one) — per § SH-H037/TASK-215
- [x] `x => true` still works as the explicit all-rows synonym, and an ordinary bounded filter still
      updates only its own rows — both asserted, or the fix is indistinguishable from a blanket refusal
- [x] Assertions are **counted rows**, never the absence of an exception — the probe above is the shape
      to copy
- [x] Proven able to fail
- [x] `/specs regen bulk-filter-operations` (and `store-crud-contract` if that area's globs reach the
      changed files), with the spec diff reviewed

## Out of scope

- The caching findings — [[TASK-310]] closed those; this was found while reading for it and is
  unrelated to caching.
- `PredicateScope` itself, which is correct and already used by the portable backends.

## Human test plan

- [x] N/A — mechanical; the proof is the row count after a reduces-to-everything filter.

## Measured scope — per overload, per direction (2026-09-09)

Criterion 1 answered by probe against on-disk SQLite, 3 rows seeded per case, filter
`x => !empty.Contains(x.Name)`:

| # | Overload | thrown | rows affected | verdict |
|---|---|---|---|---|
| 1 | `AsyncDataBaseBulkStore.UpdateAsync(filter, Action<T>)` | **NONE** | **3 of 3 rewritten** | **defect** |
| 2 | `AsyncDataBaseBulkStore.UpdateAsync(filter, PropertyUpdate<T>)` | `WholeTableWriteException` | 0 | already refused |
| 3 | `AsyncDataBaseBulkStore.DeleteAsync(filter)` | `WholeTableWriteException` | 0 | already refused |
| 4 | `DataBaseBulkStore.Update(filter, Action<T>)` | **NONE** | **3 of 3 rewritten** | **defect** |
| 5 | `DataBaseBulkStore.Update(filter, PropertyUpdate<T>)` | `WholeTableWriteException` | 0 | already refused |
| 6 | `DataBaseBulkStore.Delete(filter)` | `WholeTableWriteException` | 0 | already refused |

**2 of 6**, not 6 of 6 — so the task's own "very likely present on the `PropertyUpdate` and `Delete`
overloads" was wrong in the safe direction, and blanket-wiring all six would have been indistinguishable
from wiring the two that mattered. The four that refuse do so at a *different* mechanism: they emit one
conditionless statement, which SH-H002's `AddRequiredWhere` sees. The two that did not emit a `SELECT`
followed by per-row `UPDATE … WHERE Guid = @g`, each individually bounded, so nothing downstream can
recognise the scope.

**Provider coverage, measured rather than assumed.** The two defective overloads are declared on the
shared `Birko.Data.SQL` bulk stores and **no provider overrides either of them** — swept across
`Birko.Data.SQL{,.SqLite,.PostgreSQL,.MySQL,.MSSql,.TimescaleDB,.Caching}`. The single override anywhere
is `CachedAsyncDataBaseBulkStore.UpdateAsync(filter, Action<T>)`, which wraps `base.UpdateAsync`, so it
inherits the fix. A live per-provider run was therefore not the discriminator: the guard runs on the
expression before any provider code executes, and it is the same statement for all of them. That
delegation is pinned by a test in `Birko.Data.SQL.Caching.Tests` rather than left as a reading.

## Outcome

`Update(filter, Action<T>)` on both SQL bulk stores accepted a filter that *reduces* to every row —
[[TASK-137]]'s empty-`NOT IN` shape — and rewrote every row of the table with no exception, on the
framework's primary provider.

**Root cause: a hierarchy assumption, not a missing call.** [[TASK-215]] wired `RequireBoundedFilter`
into `AbstractBulkStore` / `AbstractAsyncBulkStore`, and `DataBaseBulkStore<DB,T>` /
`AsyncDataBaseBulkStore<DB,T>` derive from **neither** — they implement `IBulkStore<T>` /
`IAsyncBulkStore<T>` directly and carry their own copies of the six filter-based overloads, which is also
why they declare their own private `RequireFilter`. "Wired into the base" says nothing until you check
which bases the concrete types actually derive from.

**Fixed at the layer that removes the class of defect, not at the two call sites.** The rule had *two*
implementations (one per abstract hierarchy, differing only in the door name they cite) and one uncovered
hierarchy. Adding a third and fourth copy in the SQL layer was the smaller diff and is the shape
§ Conventions keeps recording as the cause. So the rule now lives once, in
`Birko.Data.Core/Expressions/BoundedFilterGuard.cs`, and all three declaration sites forward to it
supplying only the all-rows door name — the one thing that genuinely differs per caller (§ SH-H037 /
TASK-215: an async store has no `DeleteAll()`). Both existing copies are byte-equivalent through the
change: their previous 4-argument `WholeTableWriteException` call defaulted `explicitDoor` to exactly the
strings they now pass explicitly.

### Step 6 — proven able to fail

Four disjoint mutations, each reverted:

| Mutation | Result | Fix-dependent tests |
|---|---|---|
| **M1** remove `RequireBoundedFilter` from `AsyncDataBaseBulkStore.UpdateAsync(filter, Action)` | SqLite **3 of 363** red; Caching **1 of 25** red | `Async_UpdateWithAnAction_AndAFilterThatReducesToEveryRow_IsRefusedAndRewritesNothing`, `Async_UpdateWithAnAction_AndACollapsedOrChain_IsAlsoRefused`, `Async_TheRefusalNamesTheAsyncDoor_NotOneThatWouldNotCompile`, `A_filter_that_reduces_to_every_row_is_refused_THROUGH_the_caching_decorator` |
| **M2** remove it from `DataBaseBulkStore.Update(filter, Action)` | SqLite **3 of 363** red | `Sync_UpdateWithAnAction_AndAFilterThatReducesToEveryRow_IsRefusedAndRewritesNothing`, `Sync_UpdateWithAnAction_AndACollapsedOrChain_IsAlsoRefused`, `Sync_TheRefusalNamesTheSyncDoor` |
| **M3** make the single producer `BoundedFilterGuard.Require` a no-op | SqLite **6**, InMemory **10 of 69**, JSON **2 of 23** red | the six above plus all ten `PortableUnboundedFilterGuardTests` and both `BaseBulkUnboundedFilterGuardTests` |
| **M4** async store passes the **sync** door name | SqLite **1** red | `Async_TheRefusalNamesTheAsyncDoor_NotOneThatWouldNotCompile` |
| **M5** swap the producer's two checks so the reduction runs first | Core **3 of 102** red; SqLite **2 of 363** red | `An_explicit_all_rows_constant_is_allowed_through`, `Every_spelling_the_normalizer_folds_to_that_constant_is_treated_the_same`, `The_explicit_door_check_runs_before_the_reduction_check`, `{Async,Sync}_UpdateWithAnAction_AndAnExplicitTruePredicate_StillRewritesEveryRow` |

M3 is the one that matters beyond this task: it shows the delegation is **live** in both abstract
hierarchies rather than merely compiling, so the refactor is behaviour-preserving by measurement instead
of by inspection — 18 pre-existing tests across three suites depend on the new producer.

**Contract pins, not evidence** — these passed before the fix and are asserted so the verb family cannot
drift apart again: `Sync_DeleteWithAFilterThatReducesToEveryRow_WasAlreadyRefusedAtTheConnector`,
`Sync_UpdateWithAPropertyUpdate_AndAFilterThatReducesToEveryRow_WasAlreadyRefused`,
`Sync_UpdateWithAnAction_AndANullFilter_IsStillRefusedByRequireFilter`, and the four "doors that must stay
open" tests (explicit `x => true` sync and async, bounded filter sync and async, and `A && TRUE`). The
structural pin `TheSqlBulkStores_FilterBasedDestructiveOverloads_AreTheSixThatWereMeasured` is also a pin
rather than a prover: it fails on a **seventh** overload appearing, which is the recurrence this defect
had (absence by omission, not drift).

### The close gate found two things in this change

Recorded because both were mine, and neither would have been caught by a test:

1. **The guard had no test in its declaring project** (`verify-birko-conventions` check 5, against
   § TASK-255's rule that *a guard declared in `Birko.Data.SQL` is tested in `Birko.Data.SQL.Tests`, not
   only from its consumer's suite* — a guard whose only coverage lives downstream is one a downstream
   cleanup can delete silently). `BoundedFilterGuard` lives in `Birko.Data.Core` and every one of its 17
   tests lived in store projects. Added `Birko.Data.Core.Tests/BoundedFilterGuardTests.cs` — 16 tests,
   beside the existing `PredicateScopeTests` that establish the convention. It also earned **M5**: the
   producer's two checks are order-dependent (`ReducesToAllRows(x => true)` is *also* true, so the
   explicit-door check must run first or the documented synonym would be refused), and nothing pinned
   that ordering until this file existed.
2. **§ Architecture drift** (check 0c). The store-hierarchy and SQL-store diagrams sit next to each other
   with nothing saying they are **disjoint** — which is exactly the misreading that produced this defect,
   and it was in the rulebook the whole time. § Architecture now states it, with the measured cost.

### Blast radius of the new throw — measured, per § SH-H037

Two paths that previously succeeded now throw, so the rule *"measure the blast radius before turning
silence into a throw"* applies. Measured 2026-09-09 across the framework, its tests and all **16**
consumer repos: **0** callers pass an `Action<T>` to a filter-based destructive overload. (15 raw hits for
a filter-shaped `Update`/`Delete` are all the `IEnumerable<T>` overload, `File.Delete`/`Directory.Delete`,
or expression-bodied members.) And the throw fires only on a predicate that covers every row — which
before this change silently rewrote the table. `WholeTableWriteException` derives from
`InvalidOperationException`, so a host's existing `catch` still selects it (§ TASK-289's
grep-the-filters rule: nothing here *replaces* an exception, it adds one on a path that previously
returned normally).

### Not affected, checked rather than assumed

The SQL `Update(filter, PropertyUpdate<T>)` overloads go to `UpdateInternal`, **not** to the `Action<T>`
overload — so unlike the portable bases (where `PropertyUpdate` delegates to `Action` and § Conventions
records the resulting double evaluation) these paths gain no second guard and their refusal still comes
from the connector with the connector's own message. Confirmed by M1/M2 leaving
`Sync_UpdateWithAPropertyUpdate_AndAFilterThatReducesToEveryRow_WasAlreadyRefused` green.

### Judgement calls, and why the stricter option was rejected

- **Not a third and fourth copy of the guard**, though the task's own criterion permitted "applied where
  it is missing" and that would have been four lines. Rejected because the duplication *is* the defect's
  cause; the extraction is what makes the next hierarchy correct without being told.
- **Criterion 2's letter was unachievable and its purpose was met.** It says *"prefer wiring the existing
  `RequireBoundedFilter` producer"* — that member is `protected static` on `AbstractBulkStore` /
  `AbstractAsyncBulkStore`, and the SQL bulk stores derive from neither, so there was nothing to wire:
  the criterion was written from the same hierarchy assumption that caused the defect. Its *purpose* —
  do not write a second implementation of the rule — is satisfied by extracting the producer, which
  leaves **one** implementation where there were two. Said here rather than by quietly reinterpreting the
  criterion, per the rule that a criterion may be corrected before the work but never rewritten
  afterwards to fit the result.
- **`BoundedFilterGuard.Require` ignores a null filter** rather than refusing it, matching what both
  previous copies did. Refusing there would give one mistake two different messages, and each caller
  already has a `RequireFilter` whose `ArgumentNullException` names the same door.
- **`allRowsDoor` is a required parameter with no default**, though defaulting it to the sync spelling
  would have been shorter. A default is how the async stores came to cite `DeleteAll()` in the first place
  (TASK-215), and this parameter's whole reason for existing is that the wrong default is invisible.
- **No live per-provider run.** Measured instead that no provider overrides either defective overload and
  that the guard executes on the expression before any provider code — so a PostgreSQL/MySQL/MSSql run
  would exercise the same statement on the same class. Recorded as a reasoned scope decision with its
  measurement, not as a claim of live verification.
- **The `store-lazy-initialization` spec area was deliberately not regenerated** even though its
  `Birko.Data.Stores/Abstract*.cs` glob matches the change. The edit there is a behaviour-preserving
  delegation and contradicts nothing that area asserts; per the stable-wording rule, change only what the
  code now contradicts.

### Flagged, not fixed

- **On the SQL stores the `Action<T>` all-rows door is `x => true` only.** Measured: the two SQL bulk
  stores declare `UpdateAll(PropertyUpdate<T>)` / `UpdateAllAsync(PropertyUpdate<T>, ct)` and no
  `Action<T>` form, while the portable bases declare both — so the refusal on the SQL action overload
  names a door that exists but takes a different shape than the call being refused, and the honest
  remedy left to such a caller is the `x => true` synonym, which the message does not mention. The
  portable bases have the milder half of the same thing: the door they should name exists and the message
  names its sibling. Asserted as-is (the synonym is tested sync and async) and spawned as [[TASK-330]]
  rather than widened into this task, because it is a decision about public surface and not a defect —
  nothing silently does the wrong thing either way.

## Progress log

- step 2 — picked; ranked above TASK-308 because a silent whole-table rewrite is **data corruption**,
  which key 1 places above the wrong-results tier where TASK-308's predicate-mistranslation findings sit.
  It also wins key 3 (`thrown=NONE`, measured), key 4 (contained — the producer to wire already exists)
  and key 5 decisively: it is the **only** candidate in the pool with a reproduction, measured at this
  task's own filing. TASK-308's Lucene-injection finding could rival it on key 1, but its ElasticSearch
  half has 0 measured consumer usage and all 7 of its findings are unverified harvester claims. Key 6
  inert — no story in this tree declares `theme:`, so all 81 pool candidates are undeclared. (This task
  is parented directly to the EPIC, which per the skill has no theme by construction.)
- step 3 — verified: held, and **rescoped narrower**. The filed text said the `PropertyUpdate` and
  `Delete` overloads were "very likely" affected too; measured, 4 of the 6 already refuse at the
  connector and only the two `Update(filter, Action<T>)` overloads are defective. Criteria unchanged —
  criterion 1 demanded exactly this measurement and criterion 2's "where it is missing" now has a
  measured answer.
- step 4 — layer: local, but **one layer deeper than the report**. The defect is in `Birko.Data.SQL`; the
  fix's centre of gravity is a new single producer in `Birko.Data.Core`, because the same rule already had
  two implementations and the SQL layer would have been the third.
- step 5 — fix in `Birko.Data.Core/Expressions/BoundedFilterGuard.cs` (new, registered in
  `Birko.Data.Core.projitems`), `Birko.Data.Stores/Abstract{,Async}BulkStore.cs` (delegate),
  `Birko.Data.SQL/Stores/{Async,}DataBaseBulkStore.cs` (guard wired + own forwarder); tests in
  `Birko.Data.SQL.SqLite.Tests/BulkStoreActionUpdateBoundedFilterTests.cs` (15 new) and two appended to
  `Birko.Data.SQL.Caching.Tests/CachedStoreBehaviorTests.cs`. Throwaway probe
  `ZZProbeSixOverloadsTests.cs` deleted. Eight offline suites green: Core 86, SQL 686, SqLite 363,
  InMemory 69, JSON 23, XML 18, Caching 25, SQL.Providers 8 = **1,278 / 0 failed**.
- step 6 — M1: 3 of 363 (SqLite) + 1 of 25 (Caching); M2: 3 of 363; M3 (producer no-op): 6 SqLite + 10
  InMemory + 2 JSON; M4 (wrong door name): 1 of 363. Fix-dependent and contract-pin names in § Outcome.
- step 7 — respecced `bulk-filter-operations` (producer named as `BoundedFilterGuard`; new paragraph that
  the two abstract bases do not reach the SQL hierarchy, with the measured 2-of-6 split; three new
  scenarios — SQL action overload sync+async, the four connector-covered neighbours, the caching
  decorator's delegation; the action-overload requirement now says both guards precede the read;
  delegation recorded as satisfying the repeat-the-guard requirement) and
  `filter-expression-translation` (pointer requirement extended to the new file).
  `docs/specs/.map.yml` gained `../Birko.Data.Core/Expressions/BoundedFilterGuard.cs` under
  `bulk-filter-operations`, so the new producer is covered by drift detection.
  `store-lazy-initialization` matches by glob and was deliberately left — see § Outcome.
- step 8 — close gate. `verify-birko-conventions` (reached directly, so its step 0a-c generic sweep ran
  here): two findings, **both mine, both fixed in this change** — the guard had no test in its declaring
  project (§ TASK-255), and § Architecture never said the two store hierarchies are disjoint (check 0c).
  Nullable-warning check clean: every test project in the family builds with **0 warnings, 0 errors**
  (swept all of them, not only the ones I ran). `verify-intent`: 7 of 7 criteria met, with criterion 2's
  letter recorded as unachievable and its purpose met. `code-review`: no findings beyond the two above;
  confirmed the `PropertyUpdate` overloads do not route through the `Action` one, so no path gains a
  second guard. `security-review` **applies** (a destructive-write boundary) and finds the change strictly
  a tightening — no check removed, no exception replaced, blast radius measured at 0 consumer callers.
  Eight offline suites: Core 102, SQL 686, SqLite 363, InMemory 69, JSON 23, XML 18, Caching 25,
  SQL.Providers 8 = **1,294 tests, 0 failed**, 33 new.
- step 9 — out-of-scope sweep: **2 boundary, 1 spawned, 0 declined**. Both `## Out of scope` bullets are
  boundaries (one names [[TASK-310]] as owner, the other states a deliberate limit on `PredicateScope`);
  the one work item — the `Action<T>` all-rows door — is [[TASK-330]].
- step 10 — closed `done`. Six commits: `Birko.Data.Core` fd3103c, `Birko.Data.Stores` c1af713,
  `Birko.Data.SQL` **adedea7** (`pr:`), `Birko.Data.Core.Tests` a3adfce,
  `Birko.Data.SQL.SqLite.Tests` 5e14ff3, `Birko.Data.SQL.Caching.Tests` c2ffa66, plus this aggregator
  commit carrying the task files, both specs and `CLAUDE.md`.
- step 11 — rollups. EPIC-014 stays `in-progress` (84 open tasks; TASK-329 is parented directly to it,
  so there is no STORY to close). `tasks/README.md` refreshed, and **DV9 re-measured rather than
  incremented**: 187 tasks carry `feature: FEATURE-014` and its `decisions.md` lists 125, so DV9 is
  **×62**, not the ×59 the banner carried from 2026-09-08 — the carried number was already 3 short.
  DV7's note corrected: two of its three stale areas were regenerated here, plus
  `store-lazy-initialization`, whose globs the diff reached and whose **body was deliberately left
  unchanged** (the change contradicts nothing it asserts — its requirement *"the filter guards run
  ahead of the delegated read"* is still literally true) with only its provenance re-stamped, so it
  does not sit permanently stale.
  ⚠ `docs/features/FEATURE-014.../status.md` got a **partial** refresh: its count line was
  `69 / 140 tasks done`, wrong in both numbers, now the measured `100 / 187`, plus the two new task
  lines. Its list still carries 147 of 187 tasks because 40 were filed after 2026-08-19, and that is
  now stated **on the file** rather than left to be discovered. A full `/feature status` regen is owed
  and is [[TASK-251]]'s DV9 territory, not this task's.

