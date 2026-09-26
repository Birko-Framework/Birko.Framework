---
id: TASK-495
parent: EPIC-001
feature: FEATURE-001
status: todo
priority: P2
assignee: ai
created: 2026-09-26
depends-on: []
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# `b-file-upload` and `b-date-range-picker` drop the field's label from their accessible names

## Context

This is [[TASK-494]]'s out-of-scope verdict. Those controls do not pass `label` to `fieldAria`, so 491's
`aria-labelledby` fix never reached them. Measured on 2026-09-26 from Chromium's accessibility tree
(puppeteer `accessibility.snapshot`, controls mounted on-screen):

| Markup | Computed | Verdict |
|---|---|---|
| `<b-option-group label="Plan choice">` | `radiogroup "Plan choice"` → `radio "A"`, `radio "B"` | correct, no action |
| `<b-date-range-picker label="Stay dates">` | `textbox "Start date"`, `textbox "End date"` | the field label is lost: a form with two ranges ("Stay dates", "Billing period") announces four indistinguishable fields |
| `<b-file-upload label="Invoice file">` | `button "Choose files"` | the field label is lost: every upload on a page is "Choose files" |

The fix is **not** simply to use `aria-labelledby` → `${uid}-label` here, because that would *replace* the
part names that are currently correct ("Start date", the button's verb). The name needs both halves:
`aria-labelledby="${uid}-label <part-id>"`, so it reads "Stay dates Start date" and "Invoice file Choose
files". Alternatively, wrap the parts in a named `role="group"`.

## Acceptance criteria

- [ ] `b-date-range-picker`'s two inputs compute names that include the field label and their part
      ("Stay dates" + "Start date" / "End date").
- [ ] `b-file-upload`'s control computes a name that includes the field label.
- [ ] Unlabelled instances keep their current part names, with no dangling IDREF.
- [ ] Both are added to `Birko.Web.Playground/a11y-name-check.mjs` and proven able to fail.

## Out of scope

- `b-option-group`, which is already correct.

## Human test plan

- [ ] Screen reader: Tab into a labelled date range and a labelled file upload. Each announces the field
      label.
