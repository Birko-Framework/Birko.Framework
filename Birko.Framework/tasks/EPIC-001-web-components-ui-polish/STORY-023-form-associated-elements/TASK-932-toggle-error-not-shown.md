---
id: TASK-932
parent: STORY-023
feature: FEATURE-001
status: done
priority: P2
assignee: ai
created: 2026-11-15
depends-on: []
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# `b-checkbox` / `b-switch` ignored the `error` attribute, so a failed required toggle showed nothing

Found while building [[TASK-132]] (2026-11-15): `b-form` puts a field's error on its control as an `error` attribute,
but `b-checkbox` and `b-switch` neither observed nor rendered it. With TASK-132's fix a required consent box made the
form invalid with no visible reason (seen in a rendered form: the text field showed "Name is required", the checkbox
nothing). Spawned rather than folded into TASK-132, whose criteria asked only for the attribute.

## Acceptance criteria

- [x] Both observe `error` and render it under the control, styled like a text field's error (`.toggle-error` in
      formToggleSheet), with `role="alert"`.
- [x] The input carries `aria-invalid` and `aria-describedby` pointing at the line while there is an error.
- [x] Cleared when the error goes.

## Human test plan

N/A — covered by backport-smoke (TASK-932 checks) and a rendered screenshot.

## Done (2026-11-15)

backport-smoke 348/348 (the TASK-932 checks: message text, aria link, cleared once ticked); verify.mjs 0 failing;
a11y checks pass. Birko.Web main 437a18b, **v0.4.14** (local, not pushed).
