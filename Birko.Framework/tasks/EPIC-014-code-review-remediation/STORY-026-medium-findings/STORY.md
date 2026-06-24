---
id: STORY-026
parent: EPIC-014
status: planned
created: 2026-06-18
source: CODE-REVIEW-AUDIT-2026-06-17.md
severity: medium
finding-count: 275
finding-ids: CR-M001 …
---

# Medium findings

## User story

As a maintainer, I want the **medium**-severity code-review findings triaged and the worthwhile
ones fixed, so quality issues below the high bar don't accumulate.

## Scope

The 275 medium findings `CR-M001 …` from
[`CODE-REVIEW-AUDIT-2026-06-17.md`](../../../CODE-REVIEW-AUDIT-2026-06-17.md). **Unverified** —
these are reviewer claims that were not individually adversarially re-checked, so confirm each is
real before fixing.

## Tasks

**Not pre-created.** Extract tasks from `CODE-REVIEW-AUDIT-2026-06-17.md` on demand — one task per
`CR-Mxxx` entry (verify-first), copying its ID/Title → title, Path → file:line, Detail → context,
Fix → approach, Acceptance → derive + add a regression test. Flip each finding's `Status` in the
audit (`done` / `wontfix`) as it's triaged.
