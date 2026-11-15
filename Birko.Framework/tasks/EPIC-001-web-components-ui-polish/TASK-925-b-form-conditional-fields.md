---
id: TASK-925
parent: EPIC-001
feature: null
status: verify
priority: P2
assignee: ai
created: 2026-11-15
depends-on: []
blocks: []
findings: [TASK-926]
pr: null
github-issue: null
jira-key: null
---

# b-form: conditional fields, `FormField.visibleWhen`

Asked for by Symbio (TASK-1090), through the Symbio session, 2026-11-15.

## Context

Symbio's navigation item form gets a Page / Custom URL switch (`linkKind`) with a required field behind each
(`pageGuid`, `url`). Before this the only option was the page toggling `b-form-field--hidden` itself (automations-page),
and a `required` rule on a hidden field still blocked submit.

## Decisions (mine; Symbio left the shape and the data question to Birko)

- Shape: both `{ field, equals?, in? }` (serialisable, for JSON schemas) and a function of `getValues()`. With
  neither `equals` nor `in`, the field shows while the value is not empty. `field` is a path from the form root.
- A conditionally hidden field is **left out of `validate().data`** and `validateGroup().data` (the data is what the
  user can see); `getValues()` still returns everything. `hidden: true` keeps its old behaviour (validated, in data).
- A condition on a field that is itself conditionally hidden is false (chains settle to a fixed point).

## Acceptance criteria

- [x] Re-evaluated on any field change, and after `setSchema`, `setValues` and `reset` (edit form shows the right
      field at once).
- [x] A hidden field is not validated: rules, `required`, and the control's own validity; an error on it is cleared
      when it hides.
- [x] `validate().data` behaviour decided and documented.
- [x] Hidden = `display: none` on the `[data-field]` wrapper with `b-form-field--hidden` (Playwright `toBeHidden()`).
- [x] Components README + API.md; CHANGELOG, v0.4.15; `FieldCondition` exported.

## Human test plan

- [ ] Symbio navigation item form (TASK-1090): new item: Page shown, URL hidden; Save with no page shows "Page is
      required" only. Switch to Custom URL: URL shows, Page hides, its error gone; Save with no URL shows "URL is
      required" only. Open an existing Custom URL item: URL shown at once. The saved item carries only the visible
      field.

## Built and merged (2026-11-15), not pushed

`b-form._applyVisibility()` runs at the end of every render, after `setValues` / `reset`, on each field change and at
the start of `validate()` / `validateGroup()`. Playground `form-conditional-smoke` (new suite, 17 checks): 17/17;
against Web main 14-16 failing. Full verify.mjs: only the two known TASK-922 b-time failures (one run also showed a
`multi-select-create-smoke` failure that passed on 4 reruns, on this build and on main). Components `tsc` clean.
Birko.Web main 3a42cfe, tagged **v0.4.15** locally; Playground main ca1344c. Not pushed. Worktrees removed.

Found while testing: `validateGroup()` on a nested group reports under the root's name → [[TASK-926]]. The smoke's
nested-group checks go through `validate()` for that reason; `validateGroup()` skipping a hidden field is not proven
by a test until TASK-926 is fixed.

TASK-926 fixed (2026-11-15, v0.4.16): the `validateGroup()` half of the hidden-field skip is now tested
(form-conditional-smoke 25/25).

Release consolidated (2026-11-15, the user's call): the unpushed Birko.Web tags v0.4.12-v0.4.17 were folded into one
**v0.4.12** (Birko.Web 635340e), which carries this change. Any v0.4.13-v0.4.17 above refers to a tag that no longer exists.
