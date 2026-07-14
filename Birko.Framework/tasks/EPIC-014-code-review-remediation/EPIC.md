---
id: EPIC-014
status: in-progress
created: 2026-06-18
owner: ai
affects: [Birko.Framework]
source: CODE-REVIEW-AUDIT-2026-06-17.md
---

# Code review — audit remediation

## Area of concern

Remediate the findings from the full-codebase code-review audit
([`CODE-REVIEW-AUDIT-2026-06-17.md`](../../CODE-REVIEW-AUDIT-2026-06-17.md)) — a
project-by-project multi-agent review of all 172 non-test `Birko.*` projects, with adversarial
verification of every critical & high finding.

**864 findings: 24 critical, 147 high, 275 medium, 418 low.** Every finding has a stable ID
(`CR-C01…`, `CR-H001…`, `CR-M001…`, `CR-L001…`) and a uniform field block (Path / Detail / Fix /
Acceptance / Status) so each maps 1:1 to a task.

## Structure

One **story per severity level**. The individual tasks are **not** pre-created in `tasks/` — each
story is the bucket from which tasks are extracted on demand, straight from the audit's stable-ID
entries (see the audit's "How to turn this into tasks" section). Triage a story by pulling its
findings out of the markdown when you're ready to work them, rather than mirroring 864 entries into
the tree up front.

- STORY-024 — Critical findings (24) — `CR-C01 … CR-C24`
- STORY-025 — High findings (147) — `CR-H001 … CR-H147`
- STORY-026 — Medium findings (275) — `CR-M001 …`
- STORY-027 — Low findings (418) — `CR-L001 …`
- STORY-028 — Integration-test tier (9, theme) — the Docker-gated findings pulled out of STORY-026: `CR-M089, M108, M109, M138, M159, M160, M164, M165, M166`

## Success criteria

- Every critical and high finding is either fixed (with a regression test) or explicitly marked `wontfix` with a reason, and its `Status` flipped in the audit
- Medium/low findings triaged (they are unverified reviewer claims — confirm before fixing)
- Suggested order: STORY-024 (critical) → STORY-025 (high) → STORY-026 (medium) → STORY-027 (low)
