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
- `b-form` continues to work unchanged (it collects values programmatically today)

## Notes

This is a behavior-changing, cross-cutting refactor (touches `BaseComponent` + ~12 inputs) and is a **feature** rather than a pure accessibility gap — the SR/ARIA accessibility wins were delivered separately (2026-06-15 accessibility pass). Sequenced after the `bare` attribute work since both touch the same render paths.
