---
id: TASK-035
parent: STORY-023
status: todo
priority: P3
assignee: ai
created: 2026-06-15
depends-on: [TASK-001]
blocks: []
pr: null
github-issue: null
jira-key: null
---

# Make form controls form-associated via ElementInternals

## Context

The `b-*` form inputs are Shadow DOM custom elements that do **not** currently participate in native `<form>` submission — they rely on `b-form` collecting values programmatically, or on consumers reading values via the components' JS API. An accessibility audit (2026-06-15) confirmed there is no use of `ElementInternals` / `attachInternals()` / `formAssociated` anywhere in `Birko.Web.Components`.

The same audit already shipped the SR/ARIA validation layer (a stable `BaseComponent.uid`, `fieldAria()` + `renderError()` helpers, `aria-invalid` / `aria-required` / `aria-describedby` + `role="alert"` error live-regions across all ~12 inputs). This task adds the **form-participation** layer on top of that — it is a feature, not an accessibility fix.

## Acceptance criteria

- [ ] `BaseComponent` (or an opt-in mixin/subclass) supports form association: exposes `attachInternals()` result, documents the `static formAssociated = true` requirement on subclasses
- [ ] `b-input` converted first as the reference implementation: `setFormValue()` on change, `setValidity()` mirroring the `error` + `required` attributes, `formResetCallback` / `formDisabledCallback` handled
- [ ] Remaining inputs converted: `b-textarea`, `b-select` (native + combo), `b-multi-select`, `b-tag-input`, `b-date-picker`, `b-datetime-picker`, `b-time`, `b-range`, `b-color-picker`, `b-date-range-picker`, `b-markdown-editor`
- [ ] Native `<form>` submit includes the control values in `FormData`; `form.reportValidity()` reflects component errors
- [ ] `b-form` behaviour verified unchanged (regression pass)
- [ ] Tests for `setFormValue` / `setValidity` on at least three representative components
- [ ] `Birko.Web.Components/CLAUDE.md` updated with the form-association convention

## Out of scope

- New ARIA / screen-reader semantics (already delivered in the 2026-06-15 accessibility pass)
- Cross-shadow tooltip accessible-name association (documented as a known Shadow DOM limitation)

## Human test plan

_For behaviour that unit/AI tests can't fully cover (UI/UX, edge cases, system integrations, manual verification). A human or agent runs these steps at `/tasks close` time and when `/feature review` checks the feature._

- [ ] Put converted controls inside a native `<form>`, submit it, and inspect the `FormData` (or server payload) — confirm each control's value is present under its `name`
- [ ] Mark a control invalid (set `error` / leave a `required` empty) and call `form.reportValidity()` — confirm the browser-native validation bubble appears anchored to the component
- [ ] Trigger a native form **reset** and confirm `formResetCallback` restores each control to its initial value
- [ ] Disable the form (`<fieldset disabled>`) and confirm `formDisabledCallback` propagates the disabled state into each control
- [ ] Regression: run an existing `b-form`-based screen and confirm value collection / validation behaves exactly as before (this layer must not break the programmatic path)
- [ ] Verify in Chromium + Firefox + WebKit (Safari) — `ElementInternals` / form-association support and validation-bubble behaviour differ across engines
