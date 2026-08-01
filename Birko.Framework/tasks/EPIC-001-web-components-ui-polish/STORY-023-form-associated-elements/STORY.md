---
id: STORY-023
parent: EPIC-001
status: in-progress
created: 2026-06-15
---

# Form-associated custom elements (ElementInternals)

## User story

As a developer using Birko form components inside a plain native `<form>` (no `b-form`), I want the `b-*` inputs to participate in native form submission and constraint validation, so I don't have to wire up value collection or validity by hand.

## Behaviour

- Inputs opt into form association via `static formAssociated = true` + `attachInternals()`
- `internals.setFormValue(value)` so the control's value is included in `FormData` / native submit
- `internals.setValidity({...}, message, anchor)` mirrors the `error` / `required` state, enabling native `:invalid` styling and `form.reportValidity()`
- Plays nicely with the ARIA validation wiring already shipped (aria-invalid / aria-describedby / role="alert" error region) — ElementInternals adds the *form-participation* layer, not new SR semantics
- ~~`b-form` continues to work unchanged (it collects values programmatically today)~~ — **superseded
  2026-08-01.** `b-form.validate()` now reads each control's own `validity` as well as running its schema
  rules, on a deliberately narrow whitelist (`Birko.Web.Components` `9402219`). The two layers are no longer
  independent: a control's verdict can fail the form, so this story owns the seam between them.

## The seam between the two layers

Once `validate()` reads a control's validity, "which validity" is a standing question rather than a
one-off. The rules that hold today, and the tasks that came out of asking them:

- **A whitelist, never `checkValidity()`.** `12.5` in a plain `type="number"` field is *already* natively
  invalid (implicit `step=1`) and `b-form` has always ignored it, so a blanket gate is a silent breaking
  change dressed as a bug fix. Widening the whitelist is a per-flag decision with a consumer sweep —
  [[TASK-134]].
- **Schema rules first, the control second.** One field reports one message, and every existing message
  wording is preserved. This is what stops a `max` rule and a `max` attribute double-reporting.
- **Where the two layers disagree about the same field, the disagreement is a defect.** Both known cases
  are toggles: an unchecked `required` checkbox ([[TASK-132]]) and a radio group `b-form` cannot even
  resolve ([[TASK-133]]).

## Tasks

| Task | What |
|---|---|
| [[TASK-035]] | Make the form controls form-associated via `ElementInternals` (the capability) |
| [[TASK-132]] | `required` on a checkbox / switch is inert — `b-form` counts an unchecked toggle as filled |
| [[TASK-133]] | A `radio` field's value is never collected; a `required` radio group can never validate |
| [[TASK-134]] | Decide whether `validate()` adopts the remaining validity flags, `typeMismatch` first |

## Notes

This is a behavior-changing, cross-cutting refactor (touches `BaseComponent` + ~12 inputs) and is a **feature** rather than a pure accessibility gap — the SR/ARIA accessibility wins were delivered separately (2026-06-15 accessibility pass). Sequenced after the `bare` attribute work since both touch the same render paths.
