---
id: TASK-445
parent: STORY-051
feature: FEATURE-014
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: todo
priority: P3
assignee: ai
created: 2026-09-16
depends-on: []
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# `ConflictResolution.Merge` is a silent no-op, and the enum advertises it as supported

## Context

Spawned by [[TASK-309]]'s out-of-scope sweep while fixing `SH-H010`. Not one of that task's seven findings,
and **not** a defect the spec harvest raised — found by reading the enum beside the arms being repaired.

`ConflictResolution` (`Birko.Data.Sync/Models/ConflictResolution.cs:21`) declares
`Merge` with the doc *"Merge both versions (if supported)"*. Nothing in the framework merges anything:
`ApplyConflictResolution` / `ApplyConflictResolutionAsync` have no `case` for it, so a
`CustomConflictResolver` returning `Merge` falls through — **no store write, no counter moved, no error, no
callback**. The caller is told the conflict was processed.

It is the same silent-drop family as `SH-H010` (which TASK-309 fixed) and as § SH-H037's *"a mapper that
cannot express something refuses; it never drops it quietly"* — arriving through an enum member rather than a
type dispatch. TASK-309 deliberately left it alone and documented it on the method, because **implementing a
merge is a feature, not a bug fix**, and inventing merge semantics was well outside that task's scope.

Already specced as shipped behaviour: `docs/specs/data-sync.md` § *Conflict resolution writes on the winning
side* carries the scenario **"Merge resolution is inert"**, so whichever way this is resolved the spec needs a
regen.

**Measured reach, 2026-09-16:** latent. `Birko.Data.Sync` appears in **1** consumer project file (the Sandbox
aggregator) and **0** consumer `.cs` files construct a sync provider — the same measurement TASK-309 recorded.
Hence P3: real, but nothing can hit it today.

## Acceptance criteria

- [ ] A decision is taken and recorded between: (a) **refuse** — throw or record a `SyncError` naming the
      unsupported resolution, on the grounds that a resolver that asked for a merge did not ask for nothing;
      (b) **implement** a merge hook on `SyncOptions` (e.g. `Func<T, T, T>?`) and treat `Merge` with no hook
      as (a); (c) **remove** the enum member, which is a breaking change and needs its own reach measurement.
      State *why* the other two were rejected, not just which was chosen
- [ ] Whichever is chosen, `Merge` must stop being **indistinguishable from `Skip` with the counter missing**:
      a caller has no way today to tell a merge from a dropped item
- [ ] ⚠ If (a), check the blast radius first per § SH-H037 — fail-fast is legitimate only where an opt-out
      exists and is checked first. Here the opt-out is "return something other than `Merge`", which is
      trivially available, but the reach measurement above should be re-run rather than cited
- [ ] Regression test is red-verified, with the split reported as numbers
- [ ] `docs/specs/data-sync.md` regenerated; the **"Merge resolution is inert"** scenario must change or go

## Out of scope

- The six arms `SH-H010` added for the one-sided conflicts — those are done and tested
  ([[TASK-309]]); this is only about the seventh enum member.
- `ConflictResolutionPolicy.Custom`'s own fall-through to `UseLocal` when no resolver is supplied — that is
  specced, deliberate and loud enough (§ *Conflict resolution is chosen by policy with a custom hook*).

## Human test plan

`N/A — fully covered by automated tests.` A library contract with no UI surface, and the decision is
observable entirely through store state and `SyncResult` counters.

## Implementation plan

_Populated by `/tasks plan TASK-445` — leave empty until then._
