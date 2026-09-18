---
id: TASK-113
parent: STORY-051
feature: FEATURE-014
status: done
priority: P0
assignee: ai
picked-by: fix-next
created: 2026-07-30
depends-on: []
blocks: []
pr: 86c8247
github-issue: null
jira-key: null
findings: [SH-H050, SH-H051, SH-H052]
---

# `TenantSyncProvider` scopes only saves — reads, previews and deletes span every tenant

## Context

Three CONFIRMED findings in `../Birko.Data.Sync.Tenant/Providers/TenantSyncProvider.cs`, and **one root
cause**. `ApplyTenantFiltering` wraps only `CanSaveToLocal` / `CanSaveToRemote`; its own XML doc (line ~137)
says so outright — it *"only modifies save filters, not fetch predicates"*. Everything below follows from
that sentence being true.

**SH-H052 — reads, compares and previews cross tenants (`:298`).** `GetAllItemsAsync` is called with only
`LocalFetchPredicate` / `RemoteFetchPredicate`, which tenant filtering explicitly leaves untouched. Every
tenant's rows enter `localDict` / `remoteDict`; `AnalyzeItem` never consults `BelongsToTenant`. So
`PreviewAsync` under tenant *t* enumerates and version-hashes **other tenants' entities** and reports them as
ToCreate / ToUpdate / ToDelete — and those foreign Guids also reach the knowledge store.

**SH-H051 — deletes with no tenant check at all (`:475`).** `case SyncAction.Delete:` calls
`_localStore.DeleteAsync` / `_remoteStore.DeleteAsync` **without consulting any predicate**. Because the
fetches are unscoped, an item belonging to tenant *u* that resolves to Delete under ambient tenant *t* is
deleted. `Create`, `Update` and `ApplyConflictResolutionAsync` all *do* consult the predicates — so this
reads as an omission, not a design.

**SH-H050 — knowledge and saves keyed by different tenants (`:147`).** The knowledge / last-sync calls and
`CreateKnowledgeItem` use `GetTenantGuid(options)` (lines 280, 363, 813), while the save filters apply only
`if (_tenantContext.HasTenant …)` and filter on `CurrentTenantGuid`. So
`SyncAsync(new TenantSyncOptions { TenantGuid = u })` with **no ambient tenant** — the documented way to sync
one tenant from a background job — keys knowledge to *u* while **no save predicate exists at all**, writing
every tenant's items to both stores. With ambient *t* plus options *u*, knowledge is keyed *u* while writes
are filtered to *t*.

That last one is the worst of the three, because the configuration that triggers it is the one the docs
recommend.

## Approach

The tenant term has to move from the save predicates to the **fetch** predicates, so that no foreign entity
ever enters `localDict` / `remoteDict`. That makes the read, compare, preview, delete and knowledge paths
correct by construction instead of each needing its own guard — and it is the only version of this fix that
does not leave a fourth path to be found later.

Resolve the ambient-vs-options tenant precedence **once**, in one place, and use it everywhere. Today two
different answers (`GetTenantGuid(options)` and `_tenantContext.CurrentTenantGuid`) are live in the same run;
whichever wins, the mismatch is the bug. A run with neither an ambient tenant nor `options.TenantGuid` should
**fail loudly** rather than sync everything.

Keep the delete arm's predicate consultation even after the fetches are scoped — defence in depth on a
destructive path, and it is what `Create`/`Update` already do.

## Acceptance criteria

- [x] Fetch predicates carry the tenant term; `localDict` / `remoteDict` contain **only** the target
      tenant's items, asserted directly
- [x] `PreviewAsync` under tenant *t* reports zero actions for another tenant's rows
- [x] An item belonging to tenant *u* is **never deleted** by a run scoped to tenant *t*, from either store
- [x] The `SyncAction.Delete` arm consults the save predicate as `Create`/`Update` do
- [x] Tenant resolution is single-sourced; `SyncAsync(options.TenantGuid = u)` with no ambient tenant scopes
      **writes** to *u*, not just knowledge — the documented background-job shape works correctly
- [x] Ambient *t* + options *u* is either rejected or has one defined meaning, documented in the XML doc
      — **rejected** (`TenantMismatchException`), reasoning in `ResolveTenantScope`'s remarks
- [x] A run with no tenant from either source throws rather than syncing every tenant
      — `TenantScopeRequiredException`, for tenant-scoped entity types outside an all-tenants scope
- [x] Knowledge items are keyed by the same tenant the writes were scoped to — asserted on the same run
- [x] `ApplyTenantFiltering`'s XML doc no longer says fetch predicates are untouched (it will be false)
- [x] Regression tests in `Birko.Data.Sync.Tenant.Tests` covering a two-tenant fixture for each row above
- [x] `/specs regen` for `tenant-isolation`, spec diff reviewed — **`data-sync` was not due**: its globs
      stop at `../Birko.Data.Sync*/` and never reach `../Birko.Data.Sync.Tenant/`, and it names no
      `TenantSyncProvider` requirement (criterion corrected at step 7, see the progress log)

## Out of scope

- `SH-H047` (item-level write guard trusting the caller's `TenantGuid`) — [[TASK-114]].
- `SH-H054` (nested `WithTenant` not narrowing reads) — [[TASK-115]]. Related, and worth fixing **before**
  this one if both land in the same sweep: a correct fetch predicate can still be widened by a stale
  all-tenants flag.
- `SH-H053` (`AddEventTenantScope()` binding a `Tenant.Current` that `AddTenantContext*` never registers) —
  unverified, `Birko.EventBus.Tenant`, separate task.
- The seven unverified `data-sync` highs (`SH-H008`–`SH-H014`) — same file family, different defects.

## Human test plan

N/A — covered by automated tests. A two-tenant in-memory fixture reproduces every case; the cross-tenant
consequences are assertable without a live backend.

## Outcome

**What was wrong.** A tenant-scoped sync only ever filtered what it *wrote*. What it *read* was every
tenant's rows, so a run scoped to tenant A enumerated, version-hashed, previewed, recorded knowledge for
and — on the one arm that consulted no predicate at all — **deleted** tenant B's data. Separately, two
different pieces of the same run disagreed about which tenant it was for: knowledge read
`options.TenantGuid` while the save filter read the ambient tenant, so the documented background-job call
(`SyncAsync(new TenantSyncOptions { TenantGuid = u })`, no ambient tenant) installed **no save filter at
all** and copied every tenant's items into both stores.

**The fix.** The tenant term moved onto the *fetch* predicates (`ApplyTenantFiltering` +
`BuildTenantPredicate`, composed with the caller's own predicate via `ExpressionParameterReplacer.AndAlso`),
and one new `ResolveTenantScope` is the only thing that answers "which tenant" — the fetch predicates, the
save predicates, the knowledge keys and `CreateKnowledgeItem` all take its return value.
`ApplyTenantContext`/`GetTenantGuid` are gone with it. The delete arm now consults the save predicate like
Create/Update, and `GetAllItemsAsync` re-checks `BelongsToTenant` on the materialized rows.

**Step-6 split.** Reverting the production file and its two mechanical test call-site edits: **14 of 37
failed**, 23 passed, nothing failed to compile. Fix-dependent and contract-pin test names are listed in the
progress log; the pre-fix failure messages reproduce the defect rather than merely differing
(`result.Deleted == 1` — tenant B's row really was deleted by a run scoped to A; two knowledge rows written
where one tenant was in scope). Of the 20 new checks, 6 pass pre-fix and are **pins, not evidence** — most
notably `Sync_DoesNotCopyAnotherTenantsRowsIntoTheLocalStore`, which was never broken because the save
filter was the single path the old code *did* scope.

**A passing revert exposed a worthless assertion.** The 20th test (added at step 8, below) first asserted
only the *end state* of a two-tenant loop — "both tenants' rows are present" — and **passed** the full
pre-fix revert. Pre-fix, iteration one was unscoped and copied *both* rows, which satisfies that end state
just as well as two correctly-scoped iterations do; the assertion could not tell correct scoping from no
scoping at all. Rewritten to assert after *each* iteration (A alone, then A and B), it fails the revert
properly — which is what moved the split from 13/36 to 14/37. Re-running step 6 after step 8 added a check
is what caught it: **a test written to pin a fix is not automatically evidence of it**, and only the revert
distinguishes the two.

**Judgement calls, and the stricter option that was rejected each time.**

- **Ambient *t* + explicit *u* → refused, not resolved by precedence.** From first principles the explicit
  option is the more specific instruction and should win. Rejected: that is code running inside tenant *t*'s
  scope reaching tenant *u*, which is the same shape as the `X-Tenant-Id`-vs-`tenant_id` escalation
  (SH-H048), and a silent winner makes it unobservable. The deliberate cross-tenant caller now has to say so
  (`WithAllTenantsAsync`), under which the explicit option does win — that is the per-tenant admin loop.
- **No tenant at all → throws, but only for tenant-scoped entity types.** Throwing unconditionally is
  stricter and was rejected: this provider legitimately serves models with no `TenantGuid` property, and
  there is no tenant to scope those by. Two of the pre-existing regressions use exactly such a model, so an
  unconditional throw would have broken working behaviour rather than closing a hole. `IsAllTenantsScope`
  is the escape hatch, and a pin guards it against over-tightening.
- **The post-fetch `BelongsToTenant` pass was kept even though the fetch predicate makes it redundant.**
  Redundant only if the backend honours the predicate — and this family has shipped filters a backend
  silently widened to match-all (a NEST request with a null `Query`; an empty `IN` rendered always-true). A
  test with a deliberately predicate-discarding store pins it, so deleting the pass fails rather than
  quietly moving the tenant guarantee back into the backend's hands.
- **`CreateKnowledgeItem` takes the tenant as a required parameter with no default.** A default of `null`
  would have avoided touching two existing test call sites; it would also let a future caller silently key
  knowledge to `Guid.Empty` — which is the defect. A knowledge row keyed to the wrong tenant corrupts the
  *next* run's change detection, so the damage outlives the run that wrote it.

**The review caught the fix (step-8 `code-review`).** `ApplyTenantFiltering` wrote its scoping terms back
onto the **caller's** `SyncFilterOptions<T>` — the pre-existing behaviour for the save predicates, which I
extended to the fetch predicates without noticing what that now meant. The per-tenant admin loop reuses one
instance across iterations, so iteration two would have carried `t1 && t2`, matching nothing: the loop syncs
the first tenant and then silently nothing — and it is the exact shape I had just written into the README as
sanctioned. Fail-closed, so it would never have surfaced as a leak; it would have surfaced as tenants
quietly not syncing. Now returns a copy and leaves the caller's object untouched, pinned by
`OneFilterOptionsInstanceIsReusableAcrossTenants`, verified to fail by restoring the mutation (only tenant
A's row lands). Same species as CR-M168 (mutating the caller's `SyncOptions.Direction`) — the third time
this file has been bitten by writing to a caller-owned object, which is why the resolved tenant is no longer
written back onto the options either. Suite 37/37.

**One behaviour worth knowing.** `ResolveTenantScope` runs *outside* `ExecuteSyncAsync`'s try/catch, so a
tenant refusal **propagates** rather than being folded into a `SyncResult { Success = false }` like a store
error. That is deliberate: a security refusal must not be indistinguishable from a soft, retryable sync
failure. Two tests pin it (`…IsRefused`, `…IsRefusedOnPreviewToo` assert a thrown exception, not a result).

**Flagged, not fixed.**

- The `shaped-by` evidence pass in `/specs regen` **cannot run from this repo**: every `tenant-isolation`
  source glob points into a sibling repo, so no task's `pr:` sha resolves under `git show` here. Stamped
  `shaped-by: [FEATURE-014]` (direct evidence — this task changed a file in `sources`) with
  `shaped-by-derived: false`, which is the honest value. This affects **every** area of this aggregator's
  spec tree, not just this one, and it is a property of the polyrepo shape rather than a miss in this task —
  worth its own ticket if the `shaped-by:` link is ever to mean anything here.
- `Birko.Data.Sync.Tenant` is registered in `Birko.Framework.slnx` but is imported by **no** `.csproj` in
  either tree except its own test project — so nothing but the test project compiles it. Noted while
  confirming the blast radius; not investigated.

## Progress log

- step 2 — picked; ranked above TASK-109 (`DELETE FROM "T"` on a null filter) because cross-tenant leakage
  outranks silent data loss on the blast-radius ladder, this one *also* destroys another tenant's rows, and
  its worst variant fires under the documented background-job configuration rather than needing misuse.
  TASK-126 ranked below both: it only fires when an implementor omits a filter.
- step 3 — verified all three by hand against `TenantSyncProvider.cs` (2026-08-03): **all held as written.**
  SH-H052 — `ApplyTenantFiltering` (`:139-165`) touches only `CanSaveToLocal`/`CanSaveToRemote`; the fetches
  at `:298-299` / `:389-390` pass `LocalFetchPredicate`/`RemoteFetchPredicate` untouched and `AnalyzeItem`
  (`:202`) never consults `BelongsToTenant`. SH-H051 — the `Delete` arm (`:475-486`) calls `DeleteAsync`
  with no predicate while `Create` (`:431`,`:443`), `Update` (`:459`,`:467`) and
  `ApplyConflictResolutionAsync` (`:781`,`:790`) all consult one. SH-H050 — knowledge uses
  `GetTenantGuid(options)` (`:280`,`:363`,`:813`) while the save filter gates on
  `_tenantContext.HasTenant` (`:147`), so `TenantGuid = u` with no ambient tenant installs **no save
  predicate at all**.
  One scope nuance, not worth rescoping: the foreign guids reach the knowledge *store* only on the
  `SyncAsync` path (`:504`/`:533`) — `PreviewAsync` writes no knowledge, it only reports the foreign rows.
  No extra same-root-cause findings in the file beyond the three; the seven unverified `data-sync` highs
  stay out of scope as filed.
- step 4 — layer: local. The root cause is entirely inside `Birko.Data.Sync.Tenant`; the pieces the fix
  needs (`ExpressionParameterReplacer`, `TenantMismatchException`, `TenantScopeRequiredException`,
  `ITenantContext.IsAllTenantsScope`) already exist upstream in `Birko.Data.Core` / `Birko.Data.Tenant` and
  are reused rather than re-implemented. No upstream change required. `Birko.Data.Sync.Tenant` has exactly
  one compile consumer in the whole family (its own test project) and no production consumer, so the
  internal signature changes are contained.
- step 5 — fix in `Birko.Data.Sync.Tenant/Providers/TenantSyncProvider.cs`; tests in
  `Birko.Data.Sync.Tenant.Tests/TenantSyncScopeTests.cs` (19 new) + 2 call-site updates in
  `TenantSyncKnowledgeAndConflictTests.cs`; suite 36/36 green.
- step 6 — reverted fix (production file + the two mechanical test call-site edits, both stashed):
  **14 of 37 failed**, 23 passed. Every one of the 20 new checks compiled against the pre-fix provider, so
  the split is complete — no "doesn't compile pre-fix" bucket. (Measured 13/36 first; re-measured after the
  step-8 review added `OneFilterOptionsInstanceIsReusableAcrossTenants`, whose first version **passed** the
  revert for the wrong reason — see the Outcome.)
  **Fix-dependent (14, all new):** `OneFilterOptionsInstanceIsReusableAcrossTenants`,
  `Preview_UnderOneTenant_DoesNotSeeAnotherTenantsRows`,
  `Preview_ReportsZeroActions_WhenOnlyForeignRowsExist`, `NonNullableTenantGuidEntity_IsAlsoScoped`,
  `ForeignRowsAreDropped_EvenWhenTheStoreIgnoresTheFetchPredicate`, `Delete_NeverRemovesAnotherTenantsItem`,
  `Delete_ConsultsTheSavePredicate_LikeCreateAndUpdateDo`,
  `OptionsTenant_WithNoAmbientTenant_ScopesWritesAndNotOnlyKnowledge`,
  `KnowledgeIsKeyedToTheSameTenantTheWritesWereScopedTo`, `AmbientAndOptionsTenantDisagree_IsRefused`,
  `AmbientAndOptionsTenantDisagree_IsRefusedOnPreviewToo`,
  `NoTenantFromEitherSource_Throws_RatherThanSyncingEveryTenant`,
  `AllTenantsScope_WithAnExplicitTenant_NarrowsToThatTenant`,
  `CallerFetchPredicateIsHonoured_AlongsideTheTenantTerm`.
  Failure messages reproduce the defects rather than merely differing: `Delete_NeverRemovesAnotherTenantsItem`
  reported `result.Deleted` = **1** (tenant B's row really was deleted by a run scoped to A) and
  `KnowledgeIsKeyedToTheSameTenant…` found **2** knowledge rows written where one tenant was in scope.
  **Contract pins — NOT evidence (6 new + all 17 pre-existing):**
  (`Sync_DoesNotCopyAnotherTenantsRowsIntoTheLocalStore` and five siblings, listed below.)
  `Sync_DoesNotCopyAnotherTenantsRowsIntoTheLocalStore` (passes pre-fix because the save filter was the one
  path the old code *did* scope — this row was never broken, which is exactly why it needs pinning),
  `Delete_StillHappens_ForTheScopedTenant`, `AmbientAndOptionsTenantAgree_IsAllowed`,
  `AllTenantsScope_IsTheSanctionedWayToSyncEveryTenant` (passes pre-fix for the *wrong* reason — pre-fix
  every tenant synced regardless of scope; it guards the fix against over-tightening the escape hatch),
  `EntityWithoutTenantGuid_SyncsWithNoTenantInScope`, `CallerSavePredicateIsStillHonoured`, and the 17
  earlier CR-* regressions (23 = 17 + 6 accounts for every pre-fix pass).
- step 7 — respecced `tenant-isolation` (scoped regen, stamped `42297ca`). Requirements changed, all three
  of which had documented the defects as shipped behaviour: *"Tenant-scoped sync options resolution"* →
  *"…resolves exactly one tenant per run"*; *"Tenant sync filters writes only, via the save predicates"* →
  *"…scopes the fetch predicates, not only the save predicates"*; *"Sync knowledge items are stamped with
  the effective tenant"* → *"…with the run's resolved tenant"*. Three scenarios that **asserted** the
  defects are gone (`Both stores are read across all tenants`, `Deletes bypass the save predicates
  entirely`, `A caller-supplied TenantSyncOptions is mutated in place`); 114 insertions / 50 deletions, all
  inside those three blocks, every other requirement verbatim. Nothing unintended in the diff — no
  `/tasks spawn`.
  **Acceptance-row correction:** the criterion asks for a `data-sync` regen too, but that area's globs stop
  at `../Birko.Data.Sync*/` and never reach `../Birko.Data.Sync.Tenant/` (only `tenant-isolation` lists the
  changed file), and `data-sync.md` names no `TenantSyncProvider` requirement. No regen was due there;
  regenerating it would have produced churn unrelated to this fix.
  **`shaped-by` note:** stamped `[FEATURE-014]` with `shaped-by-derived: false`. FEATURE-014 rests on direct
  evidence (this task changed a file in the area's `sources`), but the general evidence pass *cannot* run
  from this checkout: every source glob points into a sibling repo (`../Birko.X/...`), so no task's `pr:` sha
  resolves under `git show` here. That is a structural property of the aggregator-plus-polyrepo shape, not a
  skipped step, and `derived: false` is the honest stamp for it.
- step 8 — merge gate. `verify-conventions` (project-local shadow, incl. its step-0 generic sweep) raised two
  💡 items, both fixed in this change: **check #9** (7 files + a new behavioural contract → a `Recent
  Updates` entry was due) and **step 0b register-on-introduce** (two new § Conventions bullets — resolve a
  multi-source tenant once and refuse rather than pick a winner; scope the read, not just the write). Checks
  #1–#8, #10 clean: 0 CS86xx under `-warnaserror` (the only hit is a pre-existing NU1510 NuGet advisory in
  the csproj), the new test fake overrides `ReadCoreAsync` not the public `ReadAsync`, no new public surface,
  guard clauses throughout, and the full-repo doc-index drift sweep reports no `UNDOCUMENTED` project.
  `code-review` (inline — no such skill in this runtime) found **one blocker, in my own fix**: see the
  Outcome's *"the review caught the fix"* note. `security-review` (inline; the diff is squarely a security
  surface): no findings — the isolation boundary went from one layer to four, both new refusals fail closed,
  `Guid.Empty` stays a tenant value rather than a wildcard (the `ModelByTenant` rule), neither exception
  message carries a tenant identifier, and the predicate is a typed expression tree with a constant node
  rather than interpolated text.
