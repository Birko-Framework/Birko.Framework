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
| D6 | Component gaps found by consumers adopting the `b-*` catalogue ([[STORY-052]]) | approved | Backfilled: decomposed into tracked work, so the scope decision was taken. Story is `in-progress`. | 2026-07-30 | ai | [[TASK-104]], [[TASK-105]], [[TASK-107]], [[TASK-135]] |

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
- 2026-08-01 — **D6** — [[TASK-135]] added by backfill: `b-input type="decimal"` (comma-locale entry, with
  `percent` routed through it) shipped on 2026-07-31/08-01 with no task in this tree, because that line of
  work was driven from the consumer repos. Inside D6's approved scope — a Reps-origin gap in an existing
  component, the same shape as TASK-104/105/107 — so no new decision row. Filed at `review`, not `done`: the
  originating consumer still carries its **own** fix (Reps `ec69529`) rather than the component's, which is
  precisely the fork STORY-052 exists to prevent, and the mode's premise is a WebKit behaviour no headless
  harness can demonstrate.
- 2026-08-16 — **Coarse marker `idea` → `review`.** Raised by [[roadmap]] as **DV2**: every one of this
  feature's 14 tasks is now `done` except TASK-042 and TASK-135, which are both at `review`, so the stored
  marker had been lying about a feature whose build work is finished. Set from the audit rather than by
  `/feature review`, because the gate itself cannot pass yet — the two open items are the sign-off, and one
  of them needs a physical device. **Not `done`, and not "done pending"**: the word for built-but-unverified
  is `review`. Flips to `done` when TASK-042 and TASK-135 are signed off; no decision row changed state.
