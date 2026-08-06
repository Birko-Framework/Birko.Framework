---
id: FEATURE-014
created: 2026-06-18
---

# Code review — audit remediation — Decisions

> The decision ledger for stakeholders. Every idea-branch is a row with exactly one **state**. Rows are never deleted — `removed` is a state, not a deletion — so the ledger stays auditable.

## Decisions

| ID | Decision | State | Rationale | Date | By | → Tasks |
|----|----------|-------|-----------|------|----|---------|
| D1 | Critical findings ([[STORY-024]]) | approved | Backfilled: decomposed into tracked work, so the scope decision was taken. Story is `done`. | 2026-06-18 | ai | — (tracked in prose) |
| D2 | High findings ([[STORY-025]]) | approved | Backfilled: decomposed into tracked work, so the scope decision was taken. Story is `done`. | 2026-06-18 | ai | — (tracked in prose) |
| D3 | Medium findings ([[STORY-026]]) | approved | Backfilled: decomposed into tracked work, so the scope decision was taken. Story is `in-progress`. | 2026-06-18 | ai | — (tracked in prose) |
| D4 | Low findings ([[STORY-027]]) | approved | Backfilled: decomposed into tracked work, so the scope decision was taken. Story is `done`. | 2026-06-18 | ai | — (tracked in prose) |
| D5 | Integration-test tier — the Docker-gated remediation findings ([[STORY-042]]) | approved | Backfilled: decomposed into tracked work, so the scope decision was taken. Story is `planned`. | 2026-07-14 | ai | — (tracked in prose) |
| D6 | Workflow backends — unify the serialization seam (ISerializer everywhere) ([[STORY-043]]) | approved | Backfilled: decomposed into tracked work, so the scope decision was taken. Story is `done`. | 2026-07-17 | ai | — (tracked in prose) |
| D7 | Spec-harvest — high findings ([[STORY-051]]) | approved | Backfilled: decomposed into tracked work, so the scope decision was taken. Story is `in-progress`. | 2026-07-30 | ai | [[TASK-108]], [[TASK-109]], [[TASK-110]], [[TASK-111]], [[TASK-112]], [[TASK-113]], [[TASK-114]], [[TASK-115]], [[TASK-116]], [[TASK-117]], [[TASK-118]], [[TASK-125]], [[TASK-126]], [[TASK-128]], [[TASK-129]], [[TASK-137]] |
| D8 | Spec-harvest — medium findings ([[STORY-053]]) | approved | Backfilled: decomposed into tracked work, so the scope decision was taken. Story is `planned`. | 2026-07-30 | ai | — (tracked in prose) |
| D9 | Spec-harvest — low findings ([[STORY-054]]) | approved | Backfilled: decomposed into tracked work, so the scope decision was taken. Story is `planned`. | 2026-07-30 | ai | — (tracked in prose) |
| D10 | Spec-harvest — the three unrated areas ([[STORY-055]]) | approved | Backfilled: decomposed into tracked work, so the scope decision was taken. Story is `in-progress`. | 2026-07-30 | ai | — (tracked in prose) |
| D11 | Work tracked directly on the epic, outside any story | approved | Backfilled: these tasks exist and are tracked, so the scope decision was taken. | 2026-06-18 | ai | [[TASK-131]] |

**States:** `proposed` (fresh from grill, awaiting decision) · `approved` (build it) · `deferred` (not now — note unblock condition) · `changed` (approved but altered — record the delta) · `removed` (rejected / out of scope).

Only `approved` and `changed` rows generate tasks at `/feature decompose`. No row is terminal: a `deferred`/`removed` decision overturned by later evidence (incl. production feedback) is **reopened** by adding a *new* `proposed` row that links the superseded one — the old row is never deleted.

## History log

> Append-only. Every state change gets a dated line with the reason — this is the "why it changed", not just the current value.

- 2026-08-01 — **Ledger opened by backfill**, not by a `/feature new` interview. This feature was created to
  close the [[roadmap]] DV5 gap: `EPIC-014` had been tracked in `tasks/` since 2026-06-18 while this repo's
  `CLAUDE.md` committed to a family-wide `docs/features/` tree that did not exist.
- 2026-08-01 — What the rows above **do** claim: each names a real story or a real set of epic-direct tasks,
  and `→ Tasks` lists task IDs that exist. `Date` is the story's/epic's own `created`; `By` is the epic's
  recorded `owner`. State is `approved` because the work was decomposed and tracked — decomposition is
  the observable decision.
- 2026-08-01 — What they **do not** claim: no rationale text, alternative, or rejected option has been
  reconstructed. Where a real dated decision with reasoning exists it lives in `CHANGELOG.md` or
  `CLAUDE.md` § Recent Updates, which remain the authority for *why*. Rows carry no invented `deferred`
  or `removed` history, so the absence of such rows means "not recorded", not "never considered".
- 2026-08-03 — **D7** — [[TASK-137]] spawned from [[TASK-109]] while planning it: the empty-`NOT IN` → `1 = 1`
  rendering shipped 2026-07-27 is a false SQL-injection signal in query logs, by the same argument that led
  TASK-109 to *reject* `1 = 1` as its all-rows idiom. No state change — D7 is already `approved` and this is
  one more task realizing it, filed under STORY-051 alongside the other two non-`SH-` tasks the remediation
  itself produced ([[TASK-128]], [[TASK-129]]). Blocked on TASK-109, because dropping an always-true term can
  leave a `DELETE` with no `WHERE` and must reach TASK-109's deliberate-all-rows path rather than its refusal.
- 2026-08-03 — Also spawned from TASK-109, but **deliberately outside this feature**: [[TASK-138]]
  (`ReadAsync()` with no arguments does not compile — CS0121). Filed to `tasks/_loose/` with `feature: null`,
  because it is an API-ergonomics papercut rather than code-review remediation. Recorded here so the spawn is
  traceable from the feature the origin belongs to, without widening this feature's scope to cover it.
