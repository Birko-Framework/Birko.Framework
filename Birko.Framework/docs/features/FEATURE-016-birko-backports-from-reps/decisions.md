---
id: FEATURE-016
created: 2026-07-06
---

# Birko framework backports from Reps (+ cross-provider & Xaml follow-ups) — Decisions

> The decision ledger for stakeholders. Every idea-branch is a row with exactly one **state**. Rows are never deleted — `removed` is a state, not a deletion — so the ledger stays auditable.

## Decisions

| ID | Decision | State | Rationale | Date | By | → Tasks |
|----|----------|-------|-----------|------|----|---------|
| D1 | Backend / SQL framework backports (shipped) ([[STORY-037]]) | approved | Backfilled: decomposed into tracked work, so the scope decision was taken. Story is `done`. | 2026-07-06 | ai | — (tracked in prose) |
| D2 | Frontend Birko.Web backports (shipped) ([[STORY-038]]) | approved | Backfilled: decomposed into tracked work, so the scope decision was taken. Story is `done`. | 2026-07-06 | ai | — (tracked in prose) |
| D3 | Cross-provider SQL store-factory + DI backport ([[STORY-039]]) | approved | Backfilled: decomposed into tracked work, so the scope decision was taken. Story is `in-progress`. | 2026-07-06 | ai | [[TASK-042]], [[TASK-051]] |
| D4 | Web → Xaml UI / offline / device backports ([[STORY-040]]) | approved | Backfilled: decomposed into tracked work, so the scope decision was taken. Story is `done`. | 2026-07-06 | ai | [[TASK-043]], [[TASK-044]], [[TASK-045]], [[TASK-046]], [[TASK-047]], [[TASK-048]] |
| D5 | BMobileAppShell showcase / placement ([[STORY-041]]) | approved | Backfilled: decomposed into tracked work, so the scope decision was taken. Story is `done`. | 2026-07-06 | ai | [[TASK-049]], [[TASK-050]] |
| D6 | Component gaps found by consumers adopting the `b-*` catalogue ([[STORY-052]]) | approved | Backfilled: decomposed into tracked work, so the scope decision was taken. Story is `in-progress`. | 2026-07-30 | ai | [[TASK-104]], [[TASK-105]], [[TASK-107]] |

**States:** `proposed` (fresh from grill, awaiting decision) · `approved` (build it) · `deferred` (not now — note unblock condition) · `changed` (approved but altered — record the delta) · `removed` (rejected / out of scope).

Only `approved` and `changed` rows generate tasks at `/feature decompose`. No row is terminal: a `deferred`/`removed` decision overturned by later evidence (incl. production feedback) is **reopened** by adding a *new* `proposed` row that links the superseded one — the old row is never deleted.

## History log

> Append-only. Every state change gets a dated line with the reason — this is the "why it changed", not just the current value.

- 2026-08-01 — **Ledger opened by backfill**, not by a `/feature new` interview. This feature was created to
  close the [[roadmap]] DV5 gap: `EPIC-016` had been tracked in `tasks/` since 2026-07-06 while this repo's
  `CLAUDE.md` committed to a family-wide `docs/features/` tree that did not exist.
- 2026-08-01 — What the rows above **do** claim: each names a real story or a real set of epic-direct tasks,
  and `→ Tasks` lists task IDs that exist. `Date` is the story's/epic's own `created`; `By` is the epic's
  recorded `owner`. State is `approved` because the work was decomposed and tracked — decomposition is
  the observable decision.
- 2026-08-01 — What they **do not** claim: no rationale text, alternative, or rejected option has been
  reconstructed. Where a real dated decision with reasoning exists it lives in `CHANGELOG.md` or
  `CLAUDE.md` § Recent Updates, which remain the authority for *why*. Rows carry no invented `deferred`
  or `removed` history, so the absence of such rows means "not recorded", not "never considered".
