---
id: TASK-208
parent: EPIC-014
feature: FEATURE-014
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: todo
priority: P2
assignee: human
created: 2026-08-14
depends-on: []
blocks: []
related: [TASK-128, TASK-129, TASK-131]
pr: null
github-issue: null
jira-key: null
findings: []
---

# DECISION: which of `Birko.Data.SQL.View` the spec map should cover — two fixes have now landed in the excluded part

## Context

`docs/specs/.map.yml` lines 146–153 already record this question, and they name the task that would
eventually prove it:

> Three named files out of thirty. The rest of `Birko.Data.SQL.View` is in the header's out-of-scope list, so
> the 90% is deliberate rather than silent — the 2026-08-08 sweep proposed widening this to the whole tree and
> reverted it for that reason. **Worth revisiting as a DECISION (TASK-129's aggregate-view DDL defect sits in
> the excluded part), not as a coverage fix.**

That has now happened. [[TASK-129]] (2026-08-14) changed
`../Birko.Data.SQL.View/SQL/Connectors/ViewSelectSqlBuilder.cs` and
`../Birko.Data.SQL.View/SQL/DataBase_View.cs` — **neither matched by any area's globs**. Consequences,
concretely:

- The spec diff, which `/fix-next` step 7 and `/specs regen` both treat as the fix's evidence, covered only
  the part of the change that happened to live in mapped files (`SqlViewTranslator.cs` under
  `views-and-aggregation`, `Table.cs` under `schema-index-and-ddl`). The DDL emitter — where the defect
  actually was — contributed nothing to it.
- This is precisely the failure `regen`'s own step 6 warns about: *"a regen over an under-covered area
  produces a clean diff, which reads as 'nothing changed' when it means 'nothing was looked at'."* Here it
  did not read as clean only because the change also touched two mapped files. A fix confined to
  `ViewSelectSqlBuilder.cs` would have produced a spotless diff.

So the map's note is no longer hypothetical, and this task exists to settle it rather than let the note sit
for another sweep to re-propose and re-revert.

**The decision is a human's, not the loop's** — hence `assignee: human`. Widening a spec area changes what
every future regen of that area reads, and the 2026-08-08 sweep already tried the blunt version once.

## The question

Which files of `Birko.Data.SQL.View` (30-odd) belong in a spec area, and which area?

Three shapes worth costing:

1. **Name the DDL-emitting files individually**, the way the three sort-path files were added for TASK-128:
   `ViewSelectSqlBuilder.cs`, `DataBase_View.cs`, `AbstractConnector_CreateView.cs`,
   `AbstractAsyncConnector_CreateView.cs`, `AbstractConnectorBase_View.cs`. Smallest change, keeps the
   explicit out-of-scope list honest, and covers where two fixes have now landed. Repeats the pattern that
   left the current gap, though — a *sixth* file added later matches nothing.
2. **A new area, e.g. `sql-view-ddl-generation`**, owning view creation/DDL across
   `Birko.Data.SQL.View`, `Birko.Data.SQL.View.Migrations` and the provider `*.View` projects. Matches the
   skill's "granularity = capability" rule: creating a persistent view *is* a capability distinct from
   defining and querying one. Larger up-front harvest.
3. **Whole-tree glob** `../Birko.Data.SQL.View/**/*.cs` into `views-and-aggregation`. Already tried on
   2026-08-08 and reverted — record *why* it was reverted before re-proposing it, or it will be reverted a
   third time.

Related but separate: [[TASK-131]] (per-sub-repo spec trees) would change where these specs live at all, and
the answer here should not contradict it.

## Acceptance criteria

- [ ] A decision is recorded in `docs/features/FEATURE-014-code-review-remediation/decisions.md` with its
      reason, including **why the rejected shapes were rejected** — specifically why the 2026-08-08 whole-tree
      widening was reverted, so the next sweep does not re-derive it
- [ ] `.map.yml` reflects the decision, and its lines 146–153 note is replaced by the outcome rather than left
      pointing at an open question
- [ ] Any newly covered area is regenerated (`/specs regen`) and its diff reviewed — a first harvest of
      previously unmapped code is where unintended behaviour hides, so this is the point of the exercise, not
      paperwork
- [ ] The unmapped count for the `Birko.Data.SQL.View` tree is reported before and after, so the change's
      effect is a number rather than a claim
- [ ] Consistent with [[TASK-131]]'s direction, or that conflict is stated explicitly

## Out of scope

- The ~148 unmapped `.cs` files inside other already-mapped projects that `regen` step 6 reports. Same class
  of gap, much larger; this task settles one tree because two fixes have landed in it.
- Moving specs into sub-repos — [[TASK-131]].

## Human test plan

- [ ] After the map change, revert one line of TASK-129's fix in `ViewSelectSqlBuilder.cs`, run
      `/specs regen` for the affected area, and confirm the spec diff **shows it**. That is the property this
      task is buying, and it is the only way to know it was bought — a map edit that does not change what a
      regen sees is a no-op with the appearance of coverage.
