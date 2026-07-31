---
id: EPIC-014
status: in-progress
created: 2026-06-18
owner: ai
affects: [Birko.Framework]
source: [CODE-REVIEW-AUDIT-2026-06-17.md, SPEC-HARVEST-FINDINGS-2026-07-30.md]
---

# Code review — audit remediation

## Area of concern

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

## Structure

One **story per severity level per sweep**. Tasks are generally **not** pre-created — each story is the
bucket from which tasks are extracted on demand from its findings doc, rather than mirroring 1,700+
entries into the tree up front.

**The one exception is STORY-051**, where the hand-verified highs are pre-created as tasks — 11 initially,
now 13 as further findings are verified and promoted. A bounded set of traced defects is pickable work; the
reasoning against pre-creation applies to unverified claims, not to confirmed ones. **Verification is the
gate for promotion**, so a finding earns a task by being traced, not by being filed.

### Sweep 1 — `CR-*`

- STORY-024 — Critical findings (24, **done**) — `CR-C01 … CR-C24`
- STORY-025 — High findings (147, **done**) — `CR-H001 … CR-H147`
- STORY-026 — Medium findings (275, in progress) — `CR-M001 …`
- STORY-027 — Low findings (418, **done**) — `CR-L001 …`
- STORY-042 — Integration-test tier (9, theme) — the Docker-gated findings pulled out of STORY-026: `CR-M089, M108, M109, M138, M159, M160, M164, M165, M166`
- STORY-043 — Workflow serializer seam (theme, **done** 2026-07-17) — follow-on from `CR-L416`: extended `Birko.Serialization.ISerializer` to all 7 workflow backends + unified them on camelCase + family-wide null-Guid guard; not an audit-filed finding

### Sweep 2 — `SH-*`

- STORY-051 — High findings (57, in progress) — `SH-H001 … SH-H057`; **15 tasks** (TASK-108 … TASK-118, TASK-125, TASK-126, TASK-128, TASK-129) covering 16 verified findings plus two found during remediation; **5 done** (TASK-108 SH-H039, TASK-114 SH-H047, TASK-115 SH-H054, TASK-110 SH-H003 + the medium twin SH-M022, TASK-128 the view twin — no `SH-` id, so the task count runs ahead of the 4 findings closed)
- STORY-053 — Medium findings (421) — `SH-M001 … SH-M421`
- STORY-054 — Low findings (387) — `SH-L001 … SH-L387`
- STORY-055 — The three unrated areas (16) — `core-model-contracts`, `store-lazy-initialization`, `unit-of-work-and-transactions`: swept, but their findings were **never written down**. Recovery, not remediation.

## Success criteria

- Every critical and high finding from both sweeps is either fixed (with a regression test) or explicitly
  marked `wontfix` with a reason, and its `Status` flipped in the source doc
- Medium/low findings triaged (they are unverified reviewer claims — confirm before fixing)
- Every `SH-*` fix that changes behaviour is followed by a `/specs regen` of its area with the diff reviewed
- Suggested order: finish sweep 1's STORY-026, then STORY-051 (verified tasks first, then verify the
  remaining 42 highs), then STORY-053 → STORY-054. STORY-055 can run any time and is cheap.
