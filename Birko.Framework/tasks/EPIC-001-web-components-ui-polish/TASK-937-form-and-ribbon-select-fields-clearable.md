---
id: TASK-937
parent: EPIC-001
feature: null
status: done
priority: P2
assignee: ai
created: 2026-11-17
depends-on: [TASK-936]
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# `b-form` and ribbon select fields can be clearable

Follow-up to [[TASK-936]], found by surveying which select-style components accept `clearable` (the user asked,
2026-11-17). `b-select` learned `clearable` in v0.4.17, but two producers of `b-select` had no way to ask for it:

- a `b-form` select field (`FormField` had no `clearable`; `b-form` passed only `searchable` / `creatable`), so an
  optional form select could not be emptied once set;
- a `b-ribbon` select field (`RibbonField`), so a ribbon filter could not go back to no value.

Not affected: `b-country-select` and searchable selects (their × clears), `b-multi-select` (each chip's ×), the
page-size select of `b-pagination` (never empty), and `b-editable-table`, which renders its own native `<select>`.

## Decision

The user's, 2026-11-17: **opt-in** `clearable` on both, over making every non-required form select clearable by
default (which would change the look of existing optional selects in Symbio).

## Change (Birko.Web v0.4.18)

- `FormField.clearable?: boolean`; on a `select` field `b-form` renders `b-select clearable`.
- `RibbonField.clearable?: boolean`; a ribbon `select` field renders `b-select clearable`, and picking the empty
  choice emits `item-change` with `''`.
- Without `clearable` nothing changes. The look is TASK-936's, which the user confirmed.
- Docs: API.md (`FormField.clearable`, `RibbonField`), the Components CLAUDE.md ribbon line, CHANGELOG.

## Evidence

Playground backport-smoke:
- On Birko.Web main 4 of the 9 TASK-937 checks fail: neither field passes `clearable` through, and the empty option of
  both stays disabled. Setting a select's value from code can pick a disabled option, so the checks assert that the
  option is enabled, not only that the value ends up empty.
- With the change it passes 369/369, 3 of 3 runs. verify.mjs reports 0 failing checks.

keyboard-check passes 225/225, a11y-name-check and a11y-description-check pass, contextual-ribbon-check passes 13/13,
ribbon-a11y-check passes 22/22, and Components `tsc` is clean.

## Acceptance criteria

- [x] A `clearable` form select field can be emptied by the user; one without it is unchanged.
- [x] A `clearable` ribbon select field can go back to no value and reports `''` once; one without it is unchanged.

## Human test plan

1. In a consumer form, give an optional `select` field `clearable: true`, pick a value, then pick the placeholder
   entry. The field is empty and saving stores no value.
2. On a ribbon with a `select` field marked `clearable`, pick a value, then the placeholder entry. The page receives
   `item-change` with an empty value.
