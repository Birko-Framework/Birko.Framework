---
id: TASK-507
parent: null
feature: null
status: review  # 2026-09-29: Web 0d0e960; Symbio browser gate PASS (461 tests, 0 failed); awaiting the human check
priority: P1
assignee: ai
created: 2026-09-29
depends-on: []
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# `b-form` silently drops every non-field server error, and `b-input`/`b-textarea` truncate a placeholder at its first `"`

## Context

Both found by the user in consumer Symbio's TASK-820 click-through (2026-09-29), on the IoT automation create modal.
Both are framework defects that apply to EVERY create/edit form in every consumer.

1. **A server refusal not tied to a field was never shown.** `Birko.Web.Shell` `base-crud-page._save` hands a failed
   response to `showFormError(form, data)` (`Birko.Web.Components/src/form-utils`), which puts any non-field error on
   the path `_form`. `b-form.setFieldError` stored it, but `_applyErrors` only walks SCHEMA fields — and no schema
   declares `_form` — so the message was never rendered. Symptom: Save does nothing visible, the modal stays open,
   the only trace is a 400 in the console (here `Automation.TriggerMissingMetric`). Every business-rule refusal
   (duplicate, precondition, cross-field rule) was affected.
2. **A placeholder containing a double quote rendered cut off.** `b-form` escapes `placeholder` when it writes it onto
   the `<b-textarea>`/`<b-input>` host (`escapeAttr`), but both controls re-inject `getAttribute('placeholder')` — the
   UNescaped text — into their own template, so `{"key": "value"}` rendered as `{`. (`b-textarea` also re-injected
   `name` unescaped.)

## Fix

- `b-form` renders a form-level `<div class="b-form-error" role="alert" hidden>` above the fields; `_applyErrors` puts
  every error whose path no rendered field showed into it, and hides it when there are none. Field errors are
  unchanged.
- `b-textarea` (`name`, `placeholder`) and `b-input` (`placeholder`) escape with `dom-utils` `escapeAttr`.

## Acceptance criteria

- [x] A non-field server error on a create/edit modal is visible in the form; the modal stays open.
- [x] A placeholder with `"` renders whole.
- [x] Proven through Symbio `tests/ui-e2e/form-level-error.spec.ts` (Birko.Web has no test suite of its own),
      mutation-proven both ways: reverting the `b-textarea` escape → the placeholder reads `{`; hiding the banner →
      "the refusal must be on screen" red.
- [x] Symbio's full browser gate passes with the change (the banner is a new first child of every form): 461 tests,
      0 failed, 1 flaky (an unrelated rotating flake), 11.4 min, no sleep. Web commit `0d0e960`.

## Human test plan

- [ ] On any create modal, trigger a server-side business-rule refusal: the message appears above the fields.
