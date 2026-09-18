---
id: TASK-316
parent: STORY-051
feature: FEATURE-014
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: done
priority: P1
assignee: ai
picked-by: fix-next
created: 2026-09-08
depends-on: []
blocks: []
# findings: ids this task remediates, from a review/audit/spec-harvest pass (CR-* SEC-* SH-* VC-*)
findings: [SH-H034, SH-H035]
pr: "Birko.Data.ViewModel@a4ec7ac + Birko.Data.ViewModel.Tests@e9ee4ca"
github-issue: null
jira-key: null
---

# Triage the 2 remaining high spec-harvest findings in `repository-contract`

## Context

Filed by `/tasks intake --epic EPIC-014 --story STORY-051` on 2026-09-08. [[STORY-051]] had **31 task
files covering 18 of its 57 findings**, and the other **39 had no task at all** — so they were
invisible to `/tasks pick`, to the `Next up` snapshot and to [[fix-next]]. *A checklist line is filed,
not scheduled.* This is the same decomposition [[STORY-053]] received on 2026-08-09 for the medium
findings, applied a month later to the tier that outranks it: **high** means silent data loss,
cross-tenant leakage, auth bypass, or a destructive operation on the wrong rows.

This task owns the **2** open findings in the `repository-contract` area.

| Finding | Claim | Named location |
|---|---|---|
| `SH-H034` | ViewModel Update writes a fresh partially-mapped model over the whole row | `Birko.Data.ViewModel/Repositories/AbstractViewModelRepository.cs:218` |
| `SH-H035` | Hash-based "skip the write" is inert — no store reads StoreDataDelegate's return value | `Birko.Data.ViewModel/Repositories/AbstractViewModelRepository.cs:226` |

Detailed in [`SPEC-HARVEST-FINDINGS-2026-07-30.md`](../SPEC-HARVEST-FINDINGS-2026-07-30.md)
§ High severity -> `### area: repository-contract`, lines 256-274.

**The contract under review** is specced in [`docs/specs/repository-contract.md`](../../../docs/specs/repository-contract.md),
harvested from 27 source files — `../Birko.Data.Core/ViewModels/AbstractLogViewModel.cs`, `../Birko.Data.Core/ViewModels/LogViewModel.cs`, `../Birko.Data.Core/ViewModels/ModelViewModel.cs`, and more. Every one points into a **sibling repo**, so a
fix here normally lands as three commits in three repos (production, regression suite, this file) per
CLAUDE.md § Integration model.

**These are unverified harvester claims, not confirmed defects.** Confirm each against the code before
fixing. The prior to carry in comes from the 15 high findings that *were* checked by hand at harvest
time: **13 CONFIRMED** (one of them re-verified **wider** than filed), **2 CONFIRMED-NARROWER**,
**0 refuted**. So expect most to hold and a meaningful minority to need their scope corrected — and
note that "wider" has happened, so a claim is not a ceiling. Refuting on the record is a valid close; a
finding silently dropped is one the next sweep re-raises and someone re-litigates from scratch.

**Measured consumer reach, 2026-09-08** (so the fix is priced on what it protects, not on the claim's
wording): **Latent for this finding.** `Birko.Data.ViewModel` is imported by **8** consumer aggregators, but `AbstractViewModelRepository` has **0** consumer `.cs` references.

⚠ Latent is **not** a reason to skip or downweight a finding — the framework's recent history is
largely defects that stayed latent until a consumer selected the backend, and § TASK-219/256 record
that such a window *"closes the moment one does"*. It is a reason not to overstate urgency in a report.

**Why P1:** a whole-row overwrite from a partial map is corrupting, but on the ViewModel repository path only.

**Ordering constraint — the spec currently documents these defects as shipped behaviour.** The
harvest specced what the code *does*, defects included, which is exactly what let it find them. So a
behavioural fix leaves `docs/specs/repository-contract.md` lying until `/specs regen repository-contract` runs, and **that spec
diff is the fix's evidence**.

## Acceptance criteria

- [x] All 2 findings are marked **confirmed**, **confirmed-narrower**, **confirmed-wider** or **refuted** against
      the code, with the verdict and its evidence (`file:line` + the mechanism, not just the rule)
      written back into `SPEC-HARVEST-FINDINGS-2026-07-30.md`. Every `Verdict:` line names the specific
      code it traced
- [x] Confirmed findings are fixed with a regression test, or explicitly waived with a recorded reason.
      Findings sharing a root cause are fixed **together**, not one edit each
- [x] Each regression test is **red-verified**: reverting the fix fails it. Report the split as numbers,
      and name any test that passes either way as a contract pin rather than as evidence
- [x] ⚠ For a claim of *silent* loss, corruption or leakage, the assertion is the **observed
      state** — rows counted, the value read back, the tenant that could see it — never that no
      exception was thrown. § Conventions records several defects that a "did not throw" assertion
      hid, including one in this epic that hid a live failure for weeks
- [x] Any behavioural fix is followed by `/specs regen repository-contract`, with the spec diff reviewed as the
      change's evidence
- [x] A confirmed finding too large for this task is spawned via `/tasks spawn` — never left as a
      ticked box with the work undone, and never as an `## Out of scope` sentence describing work
- [x] [[STORY-051]]'s **Progress** line and its task table reflect this area's closed count

## Out of scope

- The other 37 open high findings — they belong to the other 14 per-area tasks under
  [[STORY-051]].
- The 18 high findings already covered by [[STORY-051]]'s existing 31 task files. If triage shows one of
  those fixes did **not** hold, that is a regression: file it fresh and say so, per `/tasks intake`
  § *Re-running a pass*.
- Medium-severity findings in this area — [[TASK-162]] owns those.
- Low-severity findings in this area — [[TASK-193]] owns them. Where a fix closes findings across
  tiers, do it once and cross-reference; do not split one edit across two tasks.
- **Test gaps.** Test coverage was explicitly out of scope for the harvest sweep, so a missing test is
  not a finding here — only a test a confirmed fix needs.

## Human test plan

`N/A — fully covered by automated tests.` Both findings are library-contract defects in
`Birko.Data.ViewModel` with no UI surface and no consumer `.cs` caller, and every claim they make is
observable as a value read back from a store — which is what `ViewModelUpdateMergeTests` asserts, with
seven mutations showing each assertion can fail. There is nothing a human could check that the suite
does not.

## Implementation plan

_Populated by `/tasks plan TASK-316` — leave empty until then._

## Outcome

**What was wrong.** A ViewModel is a *partial* projection of its model — it cannot express the columns the
framework owns (`CreatedAt`/`UpdatedAt`, the `TenantGuid` a store wrapper injects). The ViewModel
repositories nevertheless built an update by creating a **brand-new** model and mapping the ViewModel onto
it, never reading the row being updated; every backend then persisted that model **whole**. So each update
silently reset every unmapped column to its default (`SH-H034`). Alongside it, the repository asked the
*store* for two things no store can do — honour a transform delegate's replacement instance, and skip a
write when a hash showed nothing had changed — by relying on a `StoreDataDelegate<T>` return value that,
measured, **96 invocation sites across the framework discard and 0 consume** (`SH-H035`).

**The fix.** An update maps the ViewModel onto a **detached copy of the stored row**, so the write is a
merge (`LoadModelInstanceForUpdate` / `…Async`, one producer per sync/async tree, inherited by the bulk
subclasses). The transform is applied before the store is called, so a replacement instance is persisted —
with the key assigned first, so a delegate can still read it. The dead `return null!` is gone; the write
stays unconditional.

**Both findings were CONFIRMED-WIDER.** `SH-H034` was filed against the two single-item repositories; the
two **bulk** repositories use the same helper, so **4** update paths carried it. `SH-H035` was filed naming
5 backends; it is all 96 sites.

**Step-6 split — eleven disjoint mutations, 37 tests, every one red.** A (sync single loses the merge)
**2**; B (async single) **1**; C (sync bulk) **1**; D (async bulk) **1**; E (sync merge writes to the
store-owned instance) **1**; F (async ditto) **1**; G (Create drops the transform result) **1**; H (Update
ditto) **1**; I (Create stops pre-assigning the key) **1**; J (async ditto) **1**; K (the naive skip
re-enabled) **3**. Downstream: JSON 5, XML 7, SQL 18, ElasticSearch 6 (ViewModel suites), InMemory 74,
Localization 116 — **263 tests, 0 failed**. **Contract pins, not evidence:**
`Create_still_builds_a_FRESH_model_and_does_not_merge`,
`Updating_a_view_model_whose_row_does_not_exist_behaves_as_before`, the two
`…_STILL_issues_its_write` pins, and all **17** pre-existing tests — every one of which passed against the
unfixed code, which is why both defects survived.

**⚠ The review gate rewrote this fix, and that is the session's main lesson.** The version that passed
36/36 and read cleanly had three defects a code-review pass and a security pass found between them. All
three were real, all three were mine, and none was visible from the tests:

- **It wrote to an object the store still owns.** `MapToModel(model, stored)` mutated the instance the
  store handed back — SH-H016's mechanism, and a rule **already in § Conventions** from [[TASK-313]] six
  days earlier. So the update was applied to store state *before* and *independently of* the write: a
  failed write left it mutated, and on JSON/XML the next unrelated write would flush it to disk. Worst
  part: **my own probe store detached, which is exactly what hid it** — the tests could not see the defect
  because the test double was kinder than every real backend. Fixed with `Detach`
  (`MemberwiseClone` by reflection, as TASK-313 established); it needed a *live-reference* store to prove.
- **Enabling the dormant skip was a behaviour change I should not have made.** The security pass found
  that `AuditStoreWrapper`, `TimestampStoreWrapper` and `EventSourcingStoreWrapper` all sit **inside**
  `Store.Update` in `StoreWrapperBuilder`'s **recommended** chain, so a suppressed write silently drops
  the audit stamp, the `UpdatedAt` bump and the domain event — and `VersionedStoreWrapper`'s optimistic
  check stops running. § TASK-287's rule is that a fix must not smuggle in a behaviour change. **Backed
  out**: the write is unconditional, exactly as it has always behaved, and the decision is [[TASK-453]]
  with both traps I had already measured written onto it.
- **And the skip, while it existed, had two silent-data-loss paths of its own** — a stale hash oracle that
  dropped a caller's write after a concurrent edit, and a hash refreshed before the write that made a
  retry-after-failure a no-op. Both are now **guard tests against the naive re-enable** rather than
  behaviour, which is the useful residue of having built it and taken it out.

**Judgement calls, and why the stricter option lost.**

- **Merge rather than refuse.** Refusing an update whose ViewModel cannot express every column would break
  every legitimate caller — a partial projection is the *point* of a ViewModel repository.
- **Bulk included, at a measured cost.** Fixing only the two filed paths would ship a merging single-item
  update beside a blanking bulk one — § TASK-215. The cost is **one extra read per updated entity,
  including on the bulk path**, recorded on the method rather than hidden.
- **`Read(Guid)` rather than a bulk `Contains` filter.** One round trip per row instead of one per batch,
  chosen deliberately: § TASK-218/137 record that collection-`Contains` translation is a landmine across
  these eight backends, and `Read(Guid)` reaches each store with no translator involved.
- **The key is assigned before the transform on `Create`.** Hoisting the transform out of the store
  delegate would otherwise have silently taken away something it had: `CreateCore` assigns
  `data.Guid ??= …` *before* invoking the delegate, so a transform stamping child rows or an outbox message
  could read the new key. Every store uses `??=`, so pre-assigning is honoured everywhere — checked across
  all of them, not assumed.

**Contract change, stated where a consumer meets it.** `MapToModel` may now receive a **populated** target,
so an implementation must *assign* the fields it owns rather than accumulate into them. Documented on
`MapToModel` in both trees.

⚠ **The task's inherited reach measurement was WRONG, and re-measuring at the close gate inverted the
conclusion.** It recorded *"0 consumer `.cs` references to `AbstractViewModelRepository`"* — true, and
irrelevant, because consumers never name the base: they derive from `ElasticSearchRepository<TVm,TModel>`
and `AsyncDataBaseRepository<TConnector,TVm,TModel>`, which do. Re-measured 2026-09-17 across all 16
consumer repos: **9 `override void MapToModel` implementations** in **2** repos (Affiliate × 7,
BardStudio × 2). So this defect is **live, not latent** — § TASK-283's *grep for the subscription, not the
identifier*, arriving as *grep for the override, not the base class name*. One of those consumers,
`Affiliate.Shared/Repositories/ProductRepository.cs:25`, documents the false assumption in a comment:
*"Base properties (Guid, CreatedAt, UpdatedAt are handled by base)"* — nothing handled them.

⚠ **And all nine derive from a BULK repository — the half the finding did not name.** So fixing only the
two single-item paths as filed would have left **100% of the live consumers broken** while closing the
ticket. § TASK-215 is usually argued from consistency; here it was the difference between fixing the defect
and fixing nothing. The contract change was then **checked** against all nine rather than assumed safe:
**0 of 9** accumulate into `target`.

**Flagged, not fixed** — each with the measurement or decision it needs:

- [[TASK-451]] (P2) — `StoreDataDelegate<T>` declares a return value 96 sites discard. Framework-wide.
- [[TASK-453]] (P2) — should an unchanged save skip the write? Carries both traps already measured here.
- [[TASK-454]] (P2) — a soft-deleted or tenant-hidden row reads as absent, and updating it resurrects it
  with its unmapped columns blanked. Pre-existing; this fix made the wrong premise explicit in a comment,
  so it needed an owner.
- [[TASK-452]] (P3) — the detach rule now has three implementations (two here, one in
  `Birko.Data.Localization`); converge on one producer in `Birko.Data.Core`.

**Recorded, not filed** (consequences of the merge, documented on the code): the read-then-write window
means an unmapped column changed between this caller's read and write is written back from the snapshot —
the price of not blanking it, and there is no optimistic-concurrency default to catch it; and `Update` now
refreshes the caller's ViewModel from the full stored row rather than from the partial fresh instance.

## Progress log

- step 2 — picked; ranked above TASK-314 (`migrations`, 5 findings) because SH-H034 claims silent corruption on an ordinary write path (key 1 top class + key 2 reachability), while TASK-314's worst finding needs a consumer-authored migration filter and its five findings span four backend repos (key 4).
- step 3 — verified: **both CONFIRMED-WIDER.** `SH-H034` — `LoadModelInstance` (`AbstractViewModelRepository.cs:143-148`) is `CreateModelInstance()` + `MapToModel`, i.e. a **fresh** model; the stored row is never read, and `DataBaseStore.UpdateCore:219` (`Connector.Update(data, …)`, all columns) and `AbstractInMemoryStore.UpdateCore:93` (`_items[guid] = data`) both write it whole. Filed against the two single-item repositories; the **bulk** pair uses the same helper (`AbstractBulkViewModelRepository.cs:86`), so **4** update paths blank unmapped columns, not 2. `SH-H035` — filed naming 5 backends; measured **96** `storeDelegate?.Invoke(...)` sites across the framework and **0** consume the result, so the `return null!` skip is inert everywhere and the `processDelegate` replacement is dropped on single-item `Create` and `Update` while the bulk path honours it (CR-H110, `:87`).
- step 4 — layer: **local** (`Birko.Data.ViewModel`) for both findings. The repository is the layer that builds the model an update persists and the layer that holds the hash, so both fixes belong there. The *framework-wide* half of `SH-H035` — `StoreDataDelegate<T>` declaring a return that 96 sites discard — is a different layer (`Birko.Data.Stores` + every backend) and is **spawned**, not papered over here.
- step 5 — fix in `Birko.Data.ViewModel/Repositories/{AbstractViewModelRepository,AbstractAsyncViewModelRepository,AbstractBulkViewModelRepository,AbstractAsyncBulkViewModelRepository}.cs`; tests in `Birko.Data.ViewModel.Tests/ViewModelUpdateMergeTests.cs`; suite **30/30** green (17 pre-existing + 13 new).
- step 6 — **seven disjoint mutations**, each redding a distinct named set of 30. A (sync single loses the merge) → **3 of 30**: `Sync_single_update_preserves_the_columns_the_view_model_does_not_map`, `An_unchanged_update_issues_no_write`, `The_caller_view_model_is_refreshed_from_the_merged_row_not_from_the_defaults`. B (async single) → **2**: `Async_single_update_preserves_…`, `An_unchanged_async_update_issues_no_write_and_a_changed_one_does`. C (sync bulk) → **1**: `Sync_bulk_update_preserves_…`. D (async bulk) → **1**: `Async_bulk_update_preserves_…`. E (skip removed, always write) → **2**: both unchanged-write tests. F (Create drops the transform's return) → **1**: `…_honoured_on_create`. G (Update drops it) → **1**: `…_honoured_on_update`. **Contract pins, not evidence:** `A_changed_update_still_issues_its_write` (the other side of the skip, so the guard cannot become a blanket), `Create_still_builds_a_FRESH_model_and_does_not_merge`, `Updating_a_view_model_whose_row_does_not_exist_behaves_as_before`, and all **17** pre-existing tests — every one of which passed against the unfixed code, which is why both defects survived. ⚠ `The_caller_view_model_…` was a **vacuous** test on first write (it passed under all seven mutations because the fixture ViewModel carried nothing a fresh instance would blank); it was strengthened — the ViewModel now displays `CreatedAt` without mapping it back — and became a prover rather than being relabelled as a pin.
- step 7 — respecced `repository-contract`; requirements changed: *ViewModel↔Model mapping is repository-owned* (a create builds fresh, an update merges onto the stored row; `MapToModel` must assign rather than accumulate), *ViewModel repositories compute a SHA-256 model hash whose no-op verdict never reaches the store* → **retitled** *…suppress a no-op update using a SHA-256 model hash*, and *ProcessDataDelegate is a transform whose result is honoured only on the sync bulk path* → **retitled** *…honoured on every path*. Two scenarios were inverted rather than deleted (*An unchanged entity is written anyway* → *…is not written*; *A single-item replacement instance never reaches storage* → *…reaches storage*), and three scenarios added for the merge, the missing-row fallback and the create/update split. Both retitled requirements asserted the defect in their **title**, which is why the titles moved.
- step 8 (pre-commit) — ⚠ re-measured consumer reach at the close gate and **inverted the task's inherited claim**: 0 references to the base classes, but **9 `override void MapToModel` across 2 consumer repos** (Affiliate ×7, BardStudio ×2) reaching the changed `Update` — so **live, not latent**, and one consumer's own comment documents the false assumption. Checked the contract change against all nine: **0 of 9** accumulate into `target`, so none doubles a collection. Outcome, `CLAUDE.md` and the findings doc corrected.
- step 8 (merge gate) — ⚠ the gate **rewrote the fix**. `/code-review high` and a security pass found three real defects in the version that was 36/36 green: (1) the merge wrote to the object the store handed back — SH-H016's mechanism, a rule already in § Conventions from [[TASK-313]], and **my own probe store's detaching is what hid it**; (2) enabling the dormant hash skip silently drops the audit stamp, the `UpdatedAt` bump and the domain event, because `AuditStoreWrapper` / `TimestampStoreWrapper` / `EventSourcingStoreWrapper` all sit inside `Store.Update` in `StoreWrapperBuilder`'s recommended chain — **backed out** per § TASK-287, decision filed as [[TASK-453]]; (3) hoisting the transform out of the store delegate took away the assigned `Guid` a `Create` delegate could previously read — fixed by pre-assigning the key. Findings re-verified individually rather than taken at face value: the security pass's #1 and #5 were already fixed by the rework, and its cross-tenant questions came back clean (the merge read goes through the decorator chain, and on the all-tenants path the fix *removes* a pre-existing tenant-orphaning write).
- step 8 — mutations re-run against the **final** code (the earlier numbers described a version that no longer exists): **11 disjoint mutations, 37 tests, all 11 red**. ⚠ Mutation F initially passed — the **async** detach had no test — so one was added; that is the second time this session a mutation caught a missing guard rather than a broken one. Downstream 263 tests across 7 suites, 0 failed. Spawned [[TASK-451]], [[TASK-452]], [[TASK-453]], [[TASK-454]].
- step 8 — closed **done**; production `Birko.Data.ViewModel@a4ec7ac`, tests `Birko.Data.ViewModel.Tests@e9ee4ca`.
