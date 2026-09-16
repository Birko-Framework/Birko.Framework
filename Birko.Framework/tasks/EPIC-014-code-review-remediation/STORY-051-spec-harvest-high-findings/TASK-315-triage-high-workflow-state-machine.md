---
id: TASK-315
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
findings: [SH-H056, SH-H057]
pr: null
github-issue: null
jira-key: null
---

# Triage the 2 remaining high spec-harvest findings in `workflow-state-machine`

## Context

Filed by `/tasks intake --epic EPIC-014 --story STORY-051` on 2026-09-08. [[STORY-051]] had **31 task
files covering 18 of its 57 findings**, and the other **39 had no task at all** — so they were
invisible to `/tasks pick`, to the `Next up` snapshot and to [[fix-next]]. *A checklist line is filed,
not scheduled.* This is the same decomposition [[STORY-053]] received on 2026-08-09 for the medium
findings, applied a month later to the tier that outranks it: **high** means silent data loss,
cross-tenant leakage, auth bypass, or a destructive operation on the wrong rows.

This task owns the **2** open findings in the `workflow-state-machine` area.

| Finding | Claim | Named location |
|---|---|---|
| `SH-H056` | FindByState/FindByStatus return other workflows' rows and deserialize them into TData | `Birko.Workflow.SQL/SqlWorkflowInstanceStore.cs:76` |
| `SH-H057` | SaveAsync overwrites a record's WorkflowName and payload without checking it belongs to this workflow | `Birko.Workflow.SQL/SqlWorkflowInstanceStore.cs:52` |

Detailed in [`SPEC-HARVEST-FINDINGS-2026-07-30.md`](../SPEC-HARVEST-FINDINGS-2026-07-30.md)
§ High severity -> `### area: workflow-state-machine`, lines 432-444.

**The contract under review** is specced in [`docs/specs/workflow-state-machine.md`](../../../docs/specs/workflow-state-machine.md),
harvested from 41 source files — `../Birko.Workflow.CosmosDB/CosmosDBWorkflowInstanceSchema.cs`, `../Birko.Workflow.CosmosDB/CosmosDBWorkflowInstanceStore.cs`, `../Birko.Workflow.CosmosDB/Models/CosmosWorkflowInstanceModel.cs`, and more. Every one points into a **sibling repo**, so a
fix here normally lands as three commits in three repos (production, regression suite, this file) per
CLAUDE.md § Integration model.

**These are unverified harvester claims, not confirmed defects.** Confirm each against the code before
fixing. The prior to carry in comes from the 15 high findings that *were* checked by hand at harvest
time: **13 CONFIRMED** (one of them re-verified **wider** than filed), **2 CONFIRMED-NARROWER**,
**0 refuted**. So expect most to hold and a meaningful minority to need their scope corrected — and
note that "wider" has happened, so a claim is not a ceiling. Refuting on the record is a valid close; a
finding silently dropped is one the next sweep re-raises and someone re-litigates from scratch.

**Measured consumer reach, 2026-09-08** (so the fix is priced on what it protects, not on the claim's
wording): **Latent.** `SqlWorkflowInstanceStore`: **0** consumer `.cs` files; `Birko.Workflow.SQL` appears only in the Sandbox aggregator.

⚠ Latent is **not** a reason to skip or downweight a finding — the framework's recent history is
largely defects that stayed latent until a consumer selected the backend, and § TASK-219/256 record
that such a window *"closes the moment one does"*. It is a reason not to overstate urgency in a report.

**Why P1:** both claims are cross-workflow row bleed within one consumer's own data — corrupting, not a tenant boundary.

**Ordering constraint — the spec currently documents these defects as shipped behaviour.** The
harvest specced what the code *does*, defects included, which is exactly what let it find them. So a
behavioural fix leaves `docs/specs/workflow-state-machine.md` lying until `/specs regen workflow-state-machine` runs, and **that spec
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
- [x] Any behavioural fix is followed by `/specs regen workflow-state-machine`, with the spec diff reviewed as the
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
- Medium-severity findings in this area — [[TASK-172]] owns those.
- Low-severity findings in this area — [[TASK-179]] owns them. Where a fix closes findings across
  tiers, do it once and cross-reference; do not split one edit across two tasks.
- **Test gaps.** Test coverage was explicitly out of scope for the harvest sweep, so a missing test is
  not a finding here — only a test a confirmed fix needs.

## Human test plan

**N/A — fully covered by automated tests.** `IWorkflowInstanceStore` is a library contract with no UI
and no network surface, and it has no consumer today (0 `.cs` files across all 16 repos), so there is
nothing a human could exercise that the 12 new tests do not. The offline-reachable backend (JSON) is
proven end to end on observed state; the six that need a live server are covered by the cross-backend
source scan, which the mutations show is the only thing that fails when one of them drops a rule.

⚠ What a human *would* need to do, if and when a consumer adopts a server backend: confirm the two
scoped `Find*` filters translate correctly on that driver. Every backend hands
`m.WorkflowName == workflowName && …` to its own translator, and § Conventions records several cases
where a driver silently widened or dropped a conjunct.

## Implementation plan

_Populated by `/tasks plan TASK-315` — leave empty until then._

## Progress log

- step 2 — picked; ranked above TASK-314 (migrations) because both carry a comparably severe claim
  (`SH-H032`'s match-all delete/update vs `SH-H056`/`SH-H057`'s cross-workflow bleed), but TASK-314's
  five findings span four unrelated backend projects with no shared root cause, while these two share
  one file and one missing predicate.
- step 3 — verified against source, both **CONFIRMED and wider than this task's table**, which named
  `Birko.Workflow.SQL` only. The harvest body already named six backends for `SH-H056` and seven for
  `SH-H057`; SQL is the exemplar, not the scope. Measured reach: `IWorkflowInstanceStore` has **0**
  callers in the framework (`WorkflowEngine` never touches it), **0** construction sites in any test,
  and **0** `.cs` files across all 16 consumer repos — the seven backend test projects cover only their
  *model*, never the store. Also found, not filed: `CosmosDBWorkflowInstanceStore` scopes `FindByState`
  / `FindByStatus` to a constructor `_workflowName` while its own `FindByWorkflowNameAsync` filters on
  the *parameter*, so Cosmos contradicts itself and disagrees with the other six.
- step 4 — layer: **local**, and the fix goes in `Birko.Workflow` (the base all seven reference), not in
  seven copies.
- step 5 — fix in `Birko.Workflow/Core/WorkflowInstanceOwnership.cs` (new, one producer) +
  `Birko.Workflow/Execution/WorkflowException.cs` (`WorkflowInstanceOwnershipException`) +
  `Birko.Workflow.projitems`, wired into the update branch of `SaveAsync` in **all seven** backends,
  each of which also loses the now-dead `existing.WorkflowName = workflowName;` reassignment.
  Tests in `Birko.Workflow.Tests/WorkflowInstanceOwnershipTests.cs` (8, producer + a cross-backend
  source scan) and `Birko.Workflow.JSON.Tests/JsonWorkflowInstanceStoreOwnershipTests.cs` (4,
  end-to-end observed state). Suites: 112/112 green across all eight workflow projects
  (Workflow 46, SQL 12, JSON 14, XML 8, MongoDB 8, RavenDB 8, ElasticSearch 8, CosmosDB 8).
- step 6 — three disjoint mutations, all reverted:
  **(A) producer made a no-op** — `6 of 60` red: 5 in `Birko.Workflow.Tests`
  (`A_save_under_a_DIFFERENT_workflow_name_is_refused_and_names_both_workflows`,
  `The_refusal_names_the_doors_this_caller_actually_has`,
  `An_EMPTY_persisted_name_is_refused_too_rather_than_silently_adopted`,
  `A_NULL_persisted_name_is_refused_and_does_not_throw_a_NullReferenceException`,
  `The_comparison_is_ORDINAL_so_a_case_difference_is_a_different_workflow`) and 1 in
  `Birko.Workflow.JSON.Tests` (`A_save_aimed_at_ANOTHER_workflows_instance_leaves_that_row_byte_for_byte_intact`).
  **(B) guard removed from MongoDB alone** — `1 of 68` red, and it is the source scan
  `Every_backend_SaveAsync_calls_the_guard_and_none_reassigns_WorkflowName`; **MongoDB's own suite
  stayed green (8/8)**, which is the evidence that the scan is the only cover the six
  offline-unreachable backends have.
  **(C) the dead `existing.WorkflowName = workflowName;` restored in XML** — `1 of 68` red, the same
  scan, via its second assertion.
  **Contract pins, not evidence** (green either way, and each is here for a stated reason):
  `A_save_under_the_SAME_workflow_name_is_allowed` and
  `An_ordinary_re_save_of_the_stores_OWN_instance_still_updates_it` (a guard that refused everything
  would satisfy the real tests — `PredicateScope`'s *a false refusal breaks working code*);
  `It_derives_from_WorkflowException_so_one_catch_selects_the_whole_family` (structural);
  `The_refused_save_creates_NOTHING_either_so_the_store_still_holds_one_row` (with the no-op the save
  simply succeeds, so the row count is 1 either way — it pins the absence of a half-written second
  row, it does not prove the refusal); `SH_H056_is_UNCHANGED_here_FindByState_still_spans_workflows`
  (asserts the deliberately-unfixed read, so a later interface change cannot land silently).
- step 5b — **SH-H056 resolved too, after asking.** The user chose the interface change:
  `FindByStateAsync` / `FindByStatusAsync` now take `string workflowName` as their first parameter,
  matching `SaveAsync(workflowName, ...)` and `FindByWorkflowNameAsync(workflowName)` — so every
  operation that needs a workflow identity takes it per call. All seven backends `AND` the name into
  the filter, and **CosmosDB's `_workflowName` field and both constructor parameters are deleted**:
  it was a second source of truth its own `FindByWorkflowNameAsync` already contradicted. Break was
  loud (`CS7036`) and landed on **exactly one** site — this task's own pin test — which was
  **inverted, not deleted** (§ TASK-211). Suites 116/116.
- step 6b — three further disjoint mutations for the read half, all reverted:
  **(D) JSON `FindByState` unscoped** — `1 of 16` red in `Birko.Workflow.JSON.Tests`
  (`SH_H056_FindByState_returns_only_ITS_OWN_workflows_rows`) **and** `1 of 48` in
  `Birko.Workflow.Tests` (the source scan), i.e. the offline backend is covered twice.
  **(E) RavenDB `FindByStatus` unscoped** — `1 of 48` red, the source scan alone; **RavenDB's own
  suite stayed green (8/8)**, which is the measurement that the scan is the only cover the six
  offline-unreachable backends have on the read side too.
  **(F) a store-level `_workflowName` reintroduced on CosmosDB** — `1 of 48` red,
  `No_backend_holds_a_store_level_workflow_name`.
  **Further contract pin:** `FindByWorkflowName_is_UNCHANGED_and_still_takes_its_name_from_the_caller`
  — green either way, and present so nobody "unifies" the surviving door into the store-scoped shape
  from symmetry.
- step 7 - respecced `workflow-state-machine` (targeted regen, stable wording). Requirements changed: *Definitions are never persisted* (method list), *Save is a non-atomic read-then-write upsert* (refusal replaces the relabel), *Find queries order by UpdatedAt descending* (two scenarios pick up the new argument); **inverted**: *State and status queries are not scoped by workflow name except on CosmosDB* -> *...are scoped to the workflow the caller names*; **added**: *A save aimed at another workflow's instance is refused, not applied*. Purpose paragraph corrected, `WorkflowInstanceOwnership.cs` added to `sources:`, stamp refreshed. Nothing unintended in the diff.
- step 7b - `CLAUDE.md` gains its `### Recent Updates` entry (convention gate check 9) and a cross-reference to the one-producer and TASK-243 funnel rules this fix applies.

## Outcome

**What was wrong.** Every one of the seven `IWorkflowInstanceStore` backends keeps all workflows — and
all payload types — in a single table or collection, yet `SaveAsync` looked a record up by
`instance.InstanceId` alone and then wrote the caller's `workflowName` over whatever was stored. So a
save aimed at another workflow's row relabelled it and overwrote its payload, state and history. The
two findings are one chain: `FindByStateAsync`/`FindByStatusAsync` filtered on state alone, so they
handed back foreign rows in the first place, and `ToInstance<TData>()` turns a foreign payload into a
*fully-defaulted* `TData` rather than throwing — so the destructive save happened with empty values and
nothing anywhere raised an error.

**What was done.**

1. `Birko.Workflow.Core.WorkflowInstanceOwnership.RequireSameWorkflow` — one producer — refuses a
   mismatch with `WorkflowInstanceOwnershipException : WorkflowException`, called by all seven backends
   before any mutation. The now-dead `existing.WorkflowName = workflowName` assignment is deleted from
   all seven.
2. `FindByStateAsync` / `FindByStatusAsync` take `string workflowName` as their first parameter on the
   interface and in all seven backends, which `AND` it into the filter. CosmosDB's `_workflowName` field
   and both of its constructor parameters are deleted.

**Findings.** `SH-H056` **CONFIRMED-WIDER**, `SH-H057` **CONFIRMED**; verdicts and evidence written
into `SPEC-HARVEST-FINDINGS-2026-07-30.md`. Spec regenerated: one requirement inverted
(*"not scoped by workflow name except on CosmosDB"* → *"scoped to the workflow the caller names"*), one
added (*"A save aimed at another workflow's instance is refused"*), the save requirement and four
scenarios rewritten, the Purpose paragraph corrected, and `WorkflowInstanceOwnership.cs` added to
`sources:`.

**Step-6 split.** Six disjoint mutations — see the Progress log for the named tests. The two that carry
the argument: making the producer a no-op reds **6 of 60**; un-wiring MongoDB alone reds **1 of 68**,
the source scan, while MongoDB's own suite stays **green at 8/8**. Five tests are **contract pins, not
evidence**, each named in the log with the reason it is there.

**Judgement calls, and why the stricter option lost.**

- **SH-H056 was asked, not decided.** It is a public API change across seven shared projects and two
  designs were defensible, so `fix-next`'s guardrail applied: `SH-H057` (unambiguous, no API change) was
  implemented first, then the fork was put to the user with a recommendation and the measurement behind
  it. The user chose the per-call parameter. Rejected alternative: adopting CosmosDB's constructor-scoped
  shape — it would have forced `SaveAsync`'s `workflowName` parameter and `FindByWorkflowNameAsync` out
  too, and it is the shape Cosmos's own third query already contradicted.
- **An empty or null persisted `WorkflowName` is refused, not adopted.** Silently adopting is the
  permissive option and would be a second rule covering a row nothing writes (`FromInstance` always sets
  the name). Recorded as a decision with its own test, per § TASK-263.
- **The comparison is `StringComparison.Ordinal`.** A case-insensitive compare would be a collation
  decision the rest of the library does not make.
- **CR-L404 is superseded, not reverted.** It made the update branch refresh `WorkflowName` so a re-save
  under a different name would not go stale; with a mismatch refused that state is unreachable. The
  refusal message names the two legitimate ways to move an instance deliberately, so the capability
  CR-L404 protected is still available — explicitly rather than by accident.
- **Nothing was widened to "fix" the interface further.** `FindByWorkflowNameAsync`'s signature and
  `SaveAsync`'s are untouched, and a test pins the former so nobody unifies it from symmetry.

**Flagged, not fixed.** Nothing was deferred out of this task. Two facts recorded for whoever picks up
this area next, neither of them work this task should have done:

- `IWorkflowInstanceStore` had **no test coverage of any store** — all seven backend test projects cover
  only their model class, which is why `SH-H057` survived: any two-workflow round-trip would have caught
  it. The 12 tests added here are the first, and they cover one backend end to end plus a source scan for
  the rest. Test gaps were out of scope for this harvest (see `## Out of scope`), so the real per-backend
  suite is **spawned as [[TASK-446]]** rather than left as this paragraph.
- `SaveAsync` remains a non-atomic read-then-write upsert with documented per-backend race behaviour.
  That is specced, deliberate, and untouched here.
- step 8 - closed done; out-of-scope sweep: 5 boundary, 1 spawned ([[TASK-446]]), 0 declined.
