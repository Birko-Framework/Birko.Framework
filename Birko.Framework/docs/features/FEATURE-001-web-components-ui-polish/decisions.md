---
id: FEATURE-001
created: 2026-05-28
---

# Birko.Web.Components — UI polish — Decisions

> The decision ledger for stakeholders. Every idea-branch is a row with exactly one **state**. Rows are never deleted — `removed` is a state, not a deletion — so the ledger stays auditable.

## Decisions

| ID | Decision | State | Rationale | Date | By | → Tasks |
|----|----------|-------|-----------|------|----|---------|
| D1 | bare attribute for inline form usage ([[STORY-001]]) | approved | Backfilled: decomposed into tracked work, so the scope decision was taken. Story is `in-progress`. | 2026-05-28 | ai | [[TASK-001]] |
| D2 | b-editable-table migration to bare components ([[STORY-002]]) | approved | Backfilled: decomposed into tracked work, so the scope decision was taken. Story is `in-progress`. | 2026-05-28 | ai | [[TASK-002]] |
| D3 | size attribute coverage ([[STORY-003]]) | approved | Backfilled: decomposed into tracked work, so the scope decision was taken. Story is `planned`. | 2026-05-28 | ai | [[TASK-003]] |
| D4 | Form-associated custom elements (ElementInternals) ([[STORY-023]]) | approved | Backfilled: decomposed into tracked work, so the scope decision was taken. Story is `in-progress`. | 2026-06-15 | ai | [[TASK-035]], [[TASK-132]], [[TASK-133]] |
| D5 | Display & disclosure components ([[STORY-028]]) | approved | Backfilled: decomposed into tracked work, so the scope decision was taken. Story is `done`. | 2026-06-19 | ai | [[TASK-039]], [[TASK-040]], [[TASK-041]] |
| D6 | Visible help text on form controls ([[STORY-050]]) | approved | Backfilled: decomposed into tracked work, so the scope decision was taken. Story is `in-progress`. | 2026-07-29 | ai | [[TASK-091]] |
| D7 | Work tracked directly on the epic, outside any story | approved | Backfilled: these tasks exist and are tracked, so the scope decision was taken. | 2026-05-28 | ai | [[TASK-053]] |
| D8 | Whether `b-form.validate()` adopts the remaining native validity flags (`typeMismatch` first) beyond the shipped whitelist | proposed | **Needs `/feature decide`.** Adopting a flag changes which inputs a form rejects, so it is a stakeholder-visible behaviour change, not a refactor — and the one flag already measured (`stepMismatch` on `type="number"`, implicit `step=1`) would have newly rejected fractional input in four shipped Symbio fields. [[TASK-134]] delivers the per-flag consumer sweep and a recommendation; it must not switch a flag on before this row is decided. | 2026-08-01 | ai | [[TASK-134]] |

**States:** `proposed` (fresh from grill, awaiting decision) · `approved` (build it) · `deferred` (not now — note unblock condition) · `changed` (approved but altered — record the delta) · `removed` (rejected / out of scope).

Only `approved` and `changed` rows generate tasks at `/feature decompose`. No row is terminal: a `deferred`/`removed` decision overturned by later evidence (incl. production feedback) is **reopened** by adding a *new* `proposed` row that links the superseded one — the old row is never deleted.

## History log

> Append-only. Every state change gets a dated line with the reason — this is the "why it changed", not just the current value.

- 2026-08-01 — **Ledger opened by backfill**, not by a `/feature new` interview. This feature was created to
  close the [[roadmap]] DV5 gap: `EPIC-001` had been tracked in `tasks/` since 2026-05-28 while this repo's
  `CLAUDE.md` committed to a family-wide `docs/features/` tree that did not exist.
- 2026-08-01 — What the rows above **do** claim: each names a real story or a real set of epic-direct tasks,
  and `→ Tasks` lists task IDs that exist. `Date` is the story's/epic's own `created`; `By` is the epic's
  recorded `owner`. State is `approved` because the work was decomposed and tracked — decomposition is
  the observable decision.
- 2026-08-01 — What they **do not** claim: no rationale text, alternative, or rejected option has been
  reconstructed. Where a real dated decision with reasoning exists it lives in `CHANGELOG.md` or
  `CLAUDE.md` § Recent Updates, which remain the authority for *why*. Rows carry no invented `deferred`
  or `removed` history, so the absence of such rows means "not recorded", not "never considered".
- 2026-08-01 — **D4** — [[TASK-132]] and [[TASK-133]] spawned while making `b-form.validate()` surface a
  control's own validity (`Birko.Web.Components` `9402219`). Both are pre-existing defects where `b-form`'s
  schema layer and a form-associated control disagree about the same field, so both fall inside D4's
  already-approved scope rather than being new: an unchecked `required` checkbox counts as filled, and a
  `radio` field is never resolved at all (its value never reaches `data`, and a `required` radio group can
  never be satisfied). Neither has a live consumer — swept all 16, zero use `required` on a toggle and zero
  use a `radio` schema field — which is why they survived and why closing them is safe now.
- 2026-08-01 — **D8 opened as `proposed`** by the same work. The whitelist that fix shipped is deliberately
  narrow, and widening it is a behaviour change a stakeholder would notice (a form starts rejecting input it
  accepted), so it does **not** ride along inside D4. [[TASK-134]] gathers the per-flag evidence; the row
  returns through `/feature decide` before any flag is adopted.
