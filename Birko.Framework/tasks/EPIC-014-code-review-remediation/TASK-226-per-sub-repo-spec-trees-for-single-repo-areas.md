---
id: TASK-226
parent: EPIC-014
feature: FEATURE-014
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: todo
priority: P3
assignee: ai
created: 2026-08-16
depends-on: []
blocks: []
related: [TASK-131]
findings: []
pr: null
github-issue: null
jira-key: null
---

# Per-sub-repo `docs/specs/` trees for the 4 single-repo areas (and the 64 unspecced projects)

## Context

Spawned from [[TASK-131]], which set out to build these trees as *the* fix for the staleness guard and
measured that they are not. Kept as its own task because the idea is still good on its own merits — it
just answers a different question than the one TASK-131 existed to answer.

**What TASK-131 measured.** Of the aggregator's 25 areas, only **4** have source globs confined to a
single sibling repo:

| Area | Repo |
|---|---|
| `store-lazy-initialization` | `Birko.Data.Stores` |
| `specifications-and-paging` | `Birko.Data.Patterns` |
| `entity-tagging` | `Birko.Data.Tagging` |
| `entity-localization` | `Birko.Data.Localization` |

The other 21 span 2–13 repos (`settings-configuration-chain` 13, `views-and-aggregation` 12,
`schema-index-and-ddl` 10, `background-jobs` 10). **A per-sub-repo tree cannot host a cross-repo area** —
that is precisely why they live at the aggregator — so this move covers 16% of the map. TASK-131 fixed
staleness for all 25 instead, with a per-sibling `source-commits:` baseline.

**Why it is still worth doing.** Two reasons that survive the measurement:

- A spec next to its own code has **one** history. Its `generated-at` and its sources move together, so no
  baseline has to be reconstructed, no amnesty applies, and the sub-repo's own `/specs verify` is
  self-contained — a contributor working only in `Birko.Data.Tagging` gets a working spec layer without
  the aggregator being checked out at all.
- The map's out-of-scope block names **64 single-repo projects with no spec tree at all**. Those are not
  competing with the aggregator for a home; they simply have none. This is the larger half of the value
  and it was never what TASK-131 was about.

## Acceptance criteria

- [ ] Decide the split for the 4 measured single-repo areas: move to the sub-repo, or leave at the
      aggregator because the contract is *conceptually* cross-cutting even though today's globs are not.
      Record the reason per area — a glob count is evidence, not the decision
- [ ] Establish the per-sub-repo shape — `Birko.X/docs/specs/.map.yml` + `<area>.md`, globs relative to
      that repo so `generated-at` and the sources share one history and **no `source-commits:` is needed**
- [ ] A moved area's spec is **removed** from the aggregator, not duplicated. Two specs for one capability
      is the "one producer" defect this codebase keeps recording, arriving in the documentation layer
- [ ] `/specs verify` run *inside* a sub-repo reports that repo's areas with no reference to the
      aggregator, and going stale is demonstrated by touching a source there
- [ ] Decide whether the 64 unspecced projects get trees now, incrementally, or on first substantive
      change — with the reason. Filing 64 empty spec layers nobody regenerates is worse than none
- [ ] [[roadmap]] DV7 evaluates sub-repo trees too, or it is recorded that a sub-repo tree is audited by
      that repo's own run and deliberately invisible to the aggregator's

## Out of scope

- The staleness mechanism itself — [[TASK-131]], landed. Do **not** reintroduce per-sub-repo trees as a
  staleness fix; they were measured at 16% coverage and the `source-commits` baseline already covers 25/25.
- The `.map.yml` prose that called these trees "the fix" — already corrected by TASK-131.

## Human test plan

N/A — `/specs verify` either reports a touched source as stale inside the sub-repo or it does not, which
an automated run observes directly.

## Implementation plan

_Populated by `/tasks plan TASK-226` — leave empty until then._
