---
id: FEATURE-009
created: 2026-05-28
---

# Birko.Communication — Remaining protocols — Decisions

> The decision ledger for stakeholders. Every idea-branch is a row with exactly one **state**. Rows are never deleted — `removed` is a state, not a deletion — so the ledger stays auditable.

## Decisions

| ID | Decision | State | Rationale | Date | By | → Tasks |
|----|----------|-------|-----------|------|----|---------|
| D1 | gRPC support ([[STORY-019]]) | approved | Backfilled: decomposed into tracked work, so the scope decision was taken. Story is `done`. | 2026-05-28 | ai | [[TASK-026]] |
| D2 | OAuth2 authorization server ([[STORY-020]]) | approved | Backfilled: decomposed into tracked work, so the scope decision was taken. Story is `done`. | 2026-05-28 | ai | [[TASK-027]] |

**States:** `proposed` (fresh from grill, awaiting decision) · `approved` (build it) · `deferred` (not now — note unblock condition) · `changed` (approved but altered — record the delta) · `removed` (rejected / out of scope).

Only `approved` and `changed` rows generate tasks at `/feature decompose`. No row is terminal: a `deferred`/`removed` decision overturned by later evidence (incl. production feedback) is **reopened** by adding a *new* `proposed` row that links the superseded one — the old row is never deleted.

## History log

> Append-only. Every state change gets a dated line with the reason — this is the "why it changed", not just the current value.

- 2026-08-01 — **Ledger opened by backfill**, not by a `/feature new` interview. This feature was created to
  close the [[roadmap]] DV5 gap: `EPIC-009` had been tracked in `tasks/` since 2026-05-28 while this repo's
  `CLAUDE.md` committed to a family-wide `docs/features/` tree that did not exist.
- 2026-08-01 — What the rows above **do** claim: each names a real story or a real set of epic-direct tasks,
  and `→ Tasks` lists task IDs that exist. `Date` is the story's/epic's own `created`; `By` is the epic's
  recorded `owner`. State is `approved` because the work was decomposed and tracked — decomposition is
  the observable decision.
- 2026-08-01 — **no spec surface.** This feature is `done` and carries tracked tasks, so [[roadmap]] DV8
  would otherwise ask why no `docs/specs/` area lists it in `shaped-by:`. The answer is recorded, not
  missing: both of its surfaces — `Birko.Communication*` (TASK-026, gRPC) and `Birko.Security.OAuth.Server`
  (TASK-027) — are named in `docs/specs/.map.yml`'s explicit **out-of-scope** block, which scopes that tree
  to cross-cutting contracts spanning several sibling repos and defers single-repo surfaces to future
  per-sub-repo spec trees ([[TASK-131]]). So the absence is a recorded scoping decision; if those areas are
  ever brought in scope, this line should be removed and the feature re-checked.
- 2026-08-01 — What they **do not** claim: no rationale text, alternative, or rejected option has been
  reconstructed. Where a real dated decision with reasoning exists it lives in `CHANGELOG.md` or
  `CLAUDE.md` § Recent Updates, which remain the authority for *why*. Rows carry no invented `deferred`
  or `removed` history, so the absence of such rows means "not recorded", not "never considered".
