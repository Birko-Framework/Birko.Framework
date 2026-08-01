---
id: FEATURE-014
created: 2026-06-18
owner: ai
# status: idea | review (built, sign-off pending) | done | dropped | superseded
status: idea
---

# Code review — audit remediation

> Stakeholder-readable. Backfilled on 2026-08-01 from [EPIC-014](../../../tasks/EPIC-014-code-review-remediation/EPIC.md),
> which predates this repo's feature tree. **Nothing here is reconstructed narrative** — the Problem
> section is the epic's own "Area of concern" text, and the decision ledger is built from its real
> stories. See [decisions.md](decisions.md) § History log for what that backfill does and does not claim.

## Problem

Remediate the findings from the framework's two full-codebase sweeps. Both record defects with stable
IDs and a uniform field block, so each entry maps to a task — but they have **separate provenance and
separate ID spaces**, deliberately, so neither renumbers the other.

### Sweep 1 — the 2026-06-17 code-review audit (`CR-*`)

[`CODE-REVIEW-AUDIT-2026-06-17.md`](../../CODE-REVIEW-AUDIT-2026-06-17.md) — a project-by-project
multi-agent review of all 172 non-test `Birko.*` projects, with adversarial verification of every
critical & high finding.

**864 findings: 24 critical, 147 high, 275 medium, 418 low.** IDs `CR-C01…`, `CR-H001…`, `CR-M001…`,
`CR-L001…`, each with Path / Detail / Fix / Acceptance / Status.

### Sweep 2 — the 2026-07-30 spec harvest (`SH-*`)

[`SPEC-HARVEST-FINDINGS-2026-07-30.md`](SPEC-HARVEST-FINDINGS-2026-07-30.md) — fallout from generating
`docs/specs/`, which meant reading 648 files across 25 cross-cutting areas at code HEAD `f3ac675`. The
specs record behaviour **as-is, defects included**, so the harvest surfaced what the code actually does.

**865 findings: 57 high, 421 medium, 387 low** (plus 16 unrecorded — see STORY-055). IDs `SH-H001…`,
`SH-M001…`, `SH-L001…`. Only 15 are hand-verified; the rest are harvester claims.

**A standing constraint unique to this sweep:** the specs currently document these defects as shipped
behaviour, so any fix must be followed by a `/specs regen` of its area with the spec diff reviewed —
that diff is the fix's evidence.

## Proposed shape

- Every critical and high finding from both sweeps is either fixed (with a regression test) or explicitly
  marked `wontfix` with a reason, and its `Status` flipped in the source doc
- Medium/low findings triaged (they are unverified reviewer claims — confirm before fixing)
- Every `SH-*` fix that changes behaviour is followed by a `/specs regen` of its area with the diff reviewed
- Suggested order: finish sweep 1's STORY-026, then STORY-051 (verified tasks first, then verify the
  remaining 42 highs), then STORY-053 → STORY-054. STORY-055 can run any time and is cheap.

## Open questions distilled from the grill

_None recorded._ This feature was backfilled from an epic, so no [[grill-me]] interview preceded it and
there are no `proposed` rows awaiting a verdict. Questions raised from here on belong in
[decisions.md](decisions.md) as new `proposed` rows.

## Out of scope (initial)

- Not recorded at the time. The epic's `affects:` list is the closest thing to a scope boundary:
  `[Birko.Framework]`.

## Prototype

**N/A — backfilled.** This feature predates the prototype step, so no prototype decision was taken at
the time and inventing one retroactively would misrepresent the record. Any *future* scope added to this
feature takes the prototype decision explicitly, as a new decision row.
