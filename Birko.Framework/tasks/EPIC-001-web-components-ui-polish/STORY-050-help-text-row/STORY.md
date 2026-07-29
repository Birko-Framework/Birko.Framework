---
id: STORY-050
parent: EPIC-001
status: in-progress
created: 2026-07-29
---

# Visible help text on form controls

## User story

As a developer building a form with Birko controls, I want to put a value or constraint **on screen** under a
field — "Goal 8000 steps", "Max 20 characters" — and have a screen reader announce it as that field's
description, without rendering my own element beside the component.

## Behaviour

- A `description` attribute renders a persistent row under the control, wired into the control's
  `aria-describedby`.
- Distinct from the existing `hint`, which stays a tooltip behind a `?` icon. A field may carry both.
- An error and a description coexist: both are announced, error first; visually description then error.
- `bare` drops the row, consistent with how it drops the error row.

## Notes

The accessibility gap is the point, not the styling. Because the real control is in shadow DOM, a sibling
element rendered by the consuming page can **never** be referenced by `aria-describedby` — so before this,
help text was either invisible (a tooltip) or inaccessible (a page-rendered span). Sequenced after
STORY-001 (`bare`), which established `renderField` as the single owner of the stacked chrome.
