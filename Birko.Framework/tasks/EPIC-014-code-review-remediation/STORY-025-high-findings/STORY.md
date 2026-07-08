---
id: STORY-025
parent: EPIC-014
status: in-progress
created: 2026-06-18
source: CODE-REVIEW-AUDIT-2026-06-17.md
severity: high
finding-count: 147
finding-ids: CR-H001 … CR-H147
---

# High findings

## Progress

**136 / 147 closed** (CR-H001 … CR-H136, contiguous) as of 2026-07-08. Remaining open: **CR-H137 … CR-H147** (11).
Per-finding status is tracked in the audit doc (each finding's `Status` flips to `done` as it lands); this line is
just a rollup pulse — see [`CODE-REVIEW-AUDIT-2026-06-17.md`](../../../CODE-REVIEW-AUDIT-2026-06-17.md) for detail.

## User story

As a maintainer, I want every **high**-severity code-review finding fixed (or explicitly waived)
so the framework's serious defects — resource leaks, lazy-init bypasses, races, missing test
projects — are closed out.

## Scope

The 147 high findings `CR-H001 … CR-H147` from
[`CODE-REVIEW-AUDIT-2026-06-17.md`](../../../CODE-REVIEW-AUDIT-2026-06-17.md). All were
adversarially re-verified, so these are high-confidence.

## Tasks

**Not pre-created.** Extract tasks from `CODE-REVIEW-AUDIT-2026-06-17.md` on demand — one task per
`CR-Hxxx` entry, copying its ID/Title → title, Path → file:line, Detail → context, Fix → approach,
Acceptance → derive + add a regression test. Flip each finding's `Status` in the audit as it lands.
