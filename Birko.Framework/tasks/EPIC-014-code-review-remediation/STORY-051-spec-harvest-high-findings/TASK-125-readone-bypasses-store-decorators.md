---
id: TASK-125
parent: STORY-051
feature: FEATURE-014
status: done
priority: P1
assignee: ai
picked-by: fix-next
created: 2026-07-31
depends-on: []
blocks: []
related: [TASK-114]
# One fix, two production repos + one test repo (polyrepo — see CLAUDE.md § Integration model).
pr: >-
  Birko.Data.ViewModel@093c15b, Birko.Data.SQL.ViewModel@6017a4d,
  Birko.Data.SQL.ViewModel.Tests@dab7edd
github-issue: null
jira-key: null
findings: [SH-H036]
---

# `ReadOne` queries the connector directly, bypassing every store decorator

## Context

`../Birko.Data.SQL.ViewModel/Extensions/IDataBaseRepository.cs:20` — **CONFIRMED by hand (2026-07-31)**.

```csharp
foreach (TModel item in repository.Connector.Select<TModel, object>(typeof(TModel), filter?.Filter(), …))
```

`Connector` resolves through `GetUnwrappedStore` (`Birko.Data.Stores/StoreExtensions.cs:16`), which walks
`IStoreWrapper.GetInnerStore()` in a loop down to the **innermost** store. So every decorator is skipped —
`TenantStoreWrapper.Read` is what injects `ModelByTenant`, and going straight to the connector applies no
tenant predicate at all. `ReadOne` therefore returns the first matching row from **any** tenant. Soft-delete,
localization and audit wrappers are dropped by the same call.

This is the **read-side sibling of [[TASK-114]]**, which fixed the write side. Same root shape: a path that
looks tenant-safe from the outside and silently isn't.

## The finding's scope is wrong — correct it before fixing

`SH-H036` reads as though the bypass is one method. Checking `GetUnwrappedStore` first says otherwise:
**70 call sites across 21 data projects**. That is *not* a 70-site fix, and treating it as one would balloon
this task into a redesign:

- **69 of the 70 are the intentional escape hatch** — `XStore => Store?.GetUnwrappedStore<…>()` properties
  (`CosmosStore`, `ElasticSearchStore`, `MongoStore`, `RavenStore`, …) that exist so callers can reach
  backend-native features (bulk APIs, native queries) a portable store cannot express. Exposing them is a
  documented capability, not a defect.
- **`ReadOne` is the only general-purpose *read helper* that silently takes that path.** It is the sole
  `Extensions/` file of its kind across the ViewModel projects. That is the defect: an API whose signature
  promises a normal filtered read and whose implementation opts out of the decorator chain.

So the fix is one method. The escape-hatch properties are a **separate, smaller documentation question**
(see Out of scope).

## Reachability — state it honestly in the fix

Inside the framework, `ReadOne` has **only test callers**
(`Birko.Data.SQL.ViewModel.Tests/SyncDataBaseRepositoryTests.cs:135,148`). It is a **public extension method
shipped to consumers**, so consumer reachability is unknown and it reads as safe from the outside. That
lowers the urgency, not the correctness: do not downgrade this to "unused".

## Approach

`ReadOne` should go through the repository's **store** (and therefore its decorator chain) rather than its
connector. The decision to settle first: whether the extension can be expressed on the portable store API at
all, or whether it exists *because* it needed the connector's `Select` projection.

- If it can use the store — route it through the wrapped store and delete the connector path.
- If the connector projection is genuinely needed — the tenant (and other decorator) predicates must be
  composed onto the filter before the `Select`, which means the extension can no longer be
  decorator-agnostic. Prefer the first option; a helper that has to re-implement every decorator will drift.

**Do not "fix" this by removing `GetUnwrappedStore`.** It is load-bearing for 69 legitimate call sites.

## Acceptance criteria

- [x] `ReadOne` under an ambient tenant *t* does **not** return a row belonging to tenant *u* — asserted with
      a two-tenant fixture over a tenant-wrapped repository
- [x] The same read still returns the caller's own row unchanged (no over-filtering)
- [x] Soft-delete / localization / audit decorators are likewise not skipped — or, if only tenant is
      addressed, the remaining gaps are named explicitly in the Outcome rather than left implied
- [x] Existing `SyncDataBaseRepositoryTests` cases still pass, or their change is justified
- [x] The 69 escape-hatch `GetUnwrappedStore` properties are left alone and that decision is recorded
- [x] Regression tests in `Birko.Data.SQL.ViewModel.Tests`
- [x] `/specs regen` for `repository-contract` (and `tenant-isolation` if its scenarios move), spec diff reviewed

## Out of scope

- **Documenting the escape hatch.** Now filed as [[TASK-145]]; the async-parity gap is [[TASK-146]]. The `XStore` / `Connector` properties strip every decorator and nothing
  says so at the call site. Worth a doc note on `GetUnwrappedStore` and the CLAUDE.md files, but it is a
  documentation task, not this defect — file separately if it is not picked up here.
- `SH-H019` ([[TASK-126]]) — different subsystem, same family.
- The `WithAllTenants` + ambient-tenant contradiction ([[TASK-127]]) — a decision, not a defect.

## Outcome

**What the fix is.** A repository read that carried an ordering used to reach `repository.Connector`, and
`Connector` resolves through `GetUnwrappedStore` — which walks to the **innermost** store. Every decorator
was skipped, so under a tenant wrapper the read returned the first matching row from *any* tenant, and the
soft-delete, localization and audit wrappers were dropped by the same call. The ordered read is now an
instance method on `AbstractViewModelRepository` that reads through `Store`, and the bypassing extension is
gone.

**The part that made it survive.** There were two `ReadOne`s: a decorator-safe instance method and a
same-named extension differing only by a second argument. C# prefers an applicable instance method over an
extension, so `ReadOne(filter)` was tenant-scoped and `ReadOne(filter, orderBy)` was not — adding an
ordering to a working call silently changed its isolation, with no error and a one-argument diff.

**Why an instance method rather than a repaired extension.** `Store` is `protected`, so an extension in
another assembly cannot reach the decorated chain at all — which is exactly why the original reached for
`Connector`. The capability is not implementable safely from outside the class, so the fix had to move it,
not patch it.

**Step 6 — revert-and-split was NOT available, and that is a property of this fix.** Removing an API and
adding a safe one means the pre-fix tree does not compile against the new tests: **8 assertions fail with
`CS1501`, not with an assertion failure.** What stands in its place, each labelled for what it is:

- **Proof the bypass was live:** two shipped tests called it (with explicit type arguments) and had to be
  rewritten. Dead code does not need rewriting. My earlier sweep claimed zero callers and was **wrong** —
  a comma-counting grep cannot see `ReadOne<A,B,C,D>()`; the compiler found them.
- **Executable proof of the leak mechanism (MECHANISM PIN, not evidence):**
  `GetUnwrappedStore_strips_the_tenant_wrapper_…` shows the same query returning null through the wrapper
  and the foreign row through the unwrapped store. Unchanged behaviour — it documents *why* `Connector`
  leaked and covers the 69 sibling escape hatches nothing else asserts.
- **Contract pins, not evidence:** every 1-arg case. `ReadOne(filter)` was already safe before this task,
  so those pin behaviour the fix did not change.
- **Genuinely fix-dependent:** the 2-arg assertions — but by non-compilation, a weaker signal than a
  failing assertion, and recorded as such.

Final: `Birko.Data.SQL.ViewModel.Tests` 18/18.

**Judgement calls, and the stricter option rejected.**

- **Removed the extension rather than deprecating it.** `[Obsolete]` + delegation was considered and
  rejected: the delegation would have depended on overload resolution preferring the instance method *from
  inside the extension itself*, which is exactly the subtlety that created the defect. Leaving a same-named
  unsafe sibling in place is the failure mode, not the mitigation.
- **The file is kept as an empty documented class rather than deleted.** Deleting is cleaner and loses the
  warning at the site where the next repository extension will be written.
- **Ordering degrades instead of unwrapping.** `TenantStoreWrapper` implements `IStore<T>` but not
  `IBulkReadStore<T>`, so an ordering cannot pass through it. The overload drops the ordering and stays
  decorator-correct. Preserving the ordering would have required unwrapping — reintroducing the defect to
  keep a sort order.
- **Two shipped tests were rewritten, with assertions unchanged.** Criterion 4 allows a justified change;
  the justification is that the API they tested was removed *as the fix*, and the safe API produces the
  same observable results, so nothing was traded away.

**Flagged, not fixed.**

- **The 69 escape-hatch `GetUnwrappedStore` properties are left alone, as criterion 5 requires** — they
  exist so callers can reach backend-native features a portable store cannot express. But **nothing at
  those call sites says they strip every decorator.** That is now asserted executably by the mechanism pin
  and stated in the spec; the doc-note half remains this task's `## Out of scope` item and is still unfiled.
- **Async parity is unverified.** `AbstractAsyncViewModelRepository.ReadOneAsync(IFilter?, ct)` exists and
  there is no async ordered twin, so there is nothing to bypass today — but no test pins that, and an async
  ordered read added later could repeat this exactly.
- **Soft-delete / localization / audit decorators** are covered only by construction (reading through
  `Store` applies whatever wraps it); only the tenant decorator is asserted, per criterion 3's explicit
  allowance to name the rest rather than assert them.

## Progress log

- 2026-08-07 — **step 2 — picked, overturning the previous session's stated next pick.** That session named
  [[TASK-126]] without having read this one. Both are cross-tenant leakage — the highest severity tier in the
  pool — and both were hand-confirmed on 2026-07-31, so they tie on keys 1 and 5 and split on **reachability**:
  this bypass is in *shipped framework code* (`ReadOne` returns the first matching row from any tenant, no
  consumer error required), while TASK-126's leak needs a consumer to write a hook that omits a filter — the
  framework ships no implementation of `TagServiceBase`. Also ranked above [[TASK-117]] (`ClearAsync` issues
  `FLUSHDB` on the default path, destroying sibling projects' queues and jobs): severe and reachable by
  default configuration, but an unbounded destructive write is a tier below cross-tenant leakage, and it
  carries an unsettled key-namespace design choice.
- 2026-08-07 — **step 3 — verified: HOLDS, and narrower than filed in a way that makes it nastier.** The
  mechanism is exactly as cited: `Connector => Store?.GetUnwrappedStore<…>()?.Connector`
  (`DataBaseRepository.cs:20`) walks to the innermost store, so `Select` applies no decorator and
  `TenantStoreWrapper.Read`'s `ModelByTenant` never runs. **But there are TWO `ReadOne`s.**
  `AbstractViewModelRepository.ReadOne(IFilter<TModel>?)` (`:167`) delegates to `Read` →
  `Store.Read(filter)` and is decorator-**safe**; the bypass is a same-named *extension* taking a second
  `orderByExpr` argument. C# prefers an applicable instance method over an extension, so `repo.ReadOne(f)`
  binds to the safe one and **only the 2-arg `repo.ReadOne(f, orderBy)` reaches the bypass**.
  That is not a mitigation — it is the trap: two same-named reads, one tenant-safe and one not, selected by
  *arity*, so adding an ordering to a working call silently drops tenant scoping.
- 2026-08-07 — **the bypassing extension has ZERO callers.** Swept the framework, the test tree and all
  consumers for the 2-arg form: every hit is a false positive (commas inside the single filter argument's
  constructor). So it is unreachable dead code that is also a loaded gun, which widens the options — the
  safe fix does not have to preserve any existing call.
- 2026-08-07 — **step 4 — layer: local, but it lands in a DIFFERENT sub-repo from the defect.** The safe
  ordered read belongs on `AbstractViewModelRepository` in `Birko.Data.ViewModel`, because `Store` is
  `protected` (an extension in another assembly cannot reach the decorated chain at all — which is precisely
  why the extension grabbed `Connector`), and because "read one, ordered, through the decorators" is not
  SQL-specific. The bypass is then deleted from `Birko.Data.SQL.ViewModel`.
- 2026-08-07 — **my "zero callers" sweep was WRONG, and the compiler caught it.** Two shipped tests called
  the extension — with explicit *type arguments* (`repo.ReadOne<SyncRepo, SqLiteConnector, RepoViewModel,
  RepoModel>()`), which a comma-counting grep cannot see. So the bypass was **live and exercised**, not dead
  code. Both were rewritten onto the instance method with their assertions unchanged (criterion 4's
  "justified change"): the safe API produces the same observable results for those cases, so nothing was
  traded away to close the leak.
- 2026-08-07 — **step 5 — fix in `Birko.Data.ViewModel/Repositories/AbstractViewModelRepository.cs` (new
  decorator-safe `ReadOne(IFilter, OrderBy)`) and `Birko.Data.SQL.ViewModel/Extensions/IDataBaseRepository.cs`
  (bypass removed); tests in `Birko.Data.SQL.ViewModel.Tests` — 18/18 green.** The removed file is kept as an
  empty documented class rather than deleted, so the reasoning survives where the defect was and the next
  person adding a repository extension there meets the warning first.
- 2026-08-07 — **step 6 — revert-and-split is NOT AVAILABLE here, and that is a property of the fix, not an
  omission.** The fix *removes* an unsafe API and *adds* a safe one, so against the pre-fix tree **8
  assertions do not compile** (`CS1501: No overload for method 'ReadOne' takes 2 arguments`) rather than
  failing. Reverting produces a build error, not a split. What stands in its place:
  - **Proof the bypass was live:** the two shipped tests above called it and had to be rewritten. Dead code
    does not need rewriting.
  - **Executable proof of the leak mechanism:**
    `GetUnwrappedStore_strips_the_tenant_wrapper_which_is_why_reading_through_Connector_leaked` asserts that
    the same query returns null through the wrapper and the foreign row through the unwrapped store. It is a
    **MECHANISM PIN, not evidence** — that behaviour is unchanged and intentional; it documents *why*
    `Connector` leaked, and covers the 69 sibling escape-hatch properties nothing else asserts.
  - **Contract pins, not evidence (the 1-arg cases):** `ReadOne(filter)` was ALREADY decorator-safe before
    this task, because C# prefers the instance method over the extension. So
    `ReadOne_UnderATenant_DoesNotReturnAnotherTenants_Row` and the 1-arg half of
    `An_unfiltered_ReadOne_returns_this_tenants_row_not_whichever_is_first` pin behaviour this fix did not
    change. Recording them as proof would be exactly the mistake this step exists to prevent.
  - **Genuinely fix-dependent:** every 2-arg assertion — but they are fix-dependent by *non-compilation*,
    which is a weaker signal than a failing assertion and is labelled as such.
- 2026-08-07 — **step 7 — respecced `repository-contract`.** One requirement TITLE was the defect stated as
  contract — *"The SQL ReadOne extension bypasses the repository and queries the connector directly"* — now
  *"An ordered single read goes through the decorated store, never through the connector"*, with scenarios
  for the two-tenant case, the arity trap, the bulk-store ordering, the documented degrade, and the
  escape-hatch mechanism. **`tenant-isolation` deliberately NOT regenerated**: `TenantStoreWrapper` is
  unchanged, so none of its scenarios move — criterion 7 said "if its scenarios move". Diff reviewed:
  every change traces to an acceptance row, nothing unexplained, no findings spawned.
- 2026-08-07 — **step 8 — closed `done`; Birko.Data.ViewModel@093c15b, Birko.Data.SQL.ViewModel@6017a4d, tests@dab7edd.**

## Human test plan

N/A — covered by automated tests. A two-tenant fixture asserts the cross-tenant read is not returned.
