---
id: TASK-495
parent: EPIC-001
feature: FEATURE-001
status: review
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

### Regression from [[TASK-494]] (found 2026-09-26, while starting this task)

Two controls give each *part* its own `aria-label` **and** pass `label` to `fieldAria`. Before 494 the
`aria-label` won (the parts were named, the field label was lost). Since 494, `aria-labelledby` wins over
`aria-label`, so every part is named by the field label alone and the parts become indistinguishable:

- `b-date-range-picker` **native** mode: both inputs are named "Stay dates".
- `b-range` `mode="range"`: the From / To sliders **and** the From / To number inputs are all named
  "Volume".

494's `a11y-name-check.mjs` mounted only the default mode of each control, so it never saw a two-part
control. The fix is the same mechanism as the rest of this task, so it lands here, not as a separate task.

**Mechanism:** a `part: { id, name }` option on `fieldAria`. It emits `aria-label="<part>"`, plus (non-bare,
labelled) `aria-labelledby="${uid}-label <part id>"`, which references the element itself, so its
`aria-label` joins the name: "Stay dates Start date". Bare and labelled gives `aria-label="<label>
<part>"`. The component puts the id on the element and drops its own `aria-label`.

## Acceptance criteria

- [x] `b-date-range-picker` native mode and `b-range` `mode="range"` (sliders and number inputs): each part
      is named "<label> <part>" (regression from TASK-494).

- [x] `b-date-range-picker`'s two inputs compute names that include the field label and their part
      ("Stay dates" + "Start date" / "End date").
- [x] `b-file-upload`'s control computes a name that includes the field label.
- [x] Unlabelled instances keep their current part names, with no dangling IDREF.
- [x] Every multi-part case (both date-range modes, file upload, both `b-range` range displays) is added
      to `Birko.Web.Playground/a11y-name-check.mjs` and proven able to fail.

## Out of scope

- `b-option-group`, which is already correct.

## Human test plan

- [ ] Screen reader: Tab into a labelled date range and a labelled file upload. Each announces the field
      label.

## Progress log

- 2026-09-26 — `fieldAria` gains `part: { id, name }`: `aria-label="<part>"`, plus (non-bare, labelled)
  `aria-labelledby="${uid}-label <part id>"`, which lists the element itself; bare and labelled gives
  `aria-label="<label> <part>"`. Wired into `b-date-range-picker` (native and custom, start and end),
  `b-file-upload` (dropzone) and `b-range` range mode (From / To, sliders and number inputs). Each drops its
  own `aria-label`.
- 2026-09-26 — `a11y-name-check.mjs` gains 5 multi-part cases, plus an unlabelled date range, and the three
  controls in the dangling-IDREF sweep. Before the fix: 5 of 22 failed, including both TASK-494 regressions
  (native range "Stay dates" ×2, `b-range` "Volume" ×2). After: 22/22. `verify.mjs` 0 failing,
  `a11y-description-check` PASS, `device-fix-check` 68/68, `tsc` clean. The screen-reader step is pending → `review`.
