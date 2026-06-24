---
id: STORY-024
parent: EPIC-014
status: planned
created: 2026-06-18
source: CODE-REVIEW-AUDIT-2026-06-17.md
severity: critical
finding-count: 24
finding-ids: CR-C01 … CR-C24
---

# Critical findings

## User story

As a maintainer, I want every **critical** code-review finding fixed (or explicitly waived) so the
framework has no known correctness-breaking defects shipping.

## Scope

The 24 critical findings `CR-C01 … CR-C24` from
[`CODE-REVIEW-AUDIT-2026-06-17.md`](../../../CODE-REVIEW-AUDIT-2026-06-17.md). All were
adversarially re-verified (a second agent re-opened the cited file), so these are high-confidence.

## Tasks

**Not pre-created.** Extract tasks from `CODE-REVIEW-AUDIT-2026-06-17.md` on demand — one task per
`CR-Cxx` entry, copying its ID/Title → title, Path → file:line, Detail → context, Fix → approach,
Acceptance → derive + add a regression test. Flip each finding's `Status` in the audit as it lands.
