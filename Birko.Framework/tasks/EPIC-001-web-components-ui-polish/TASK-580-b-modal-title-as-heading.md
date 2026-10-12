---
id: TASK-580
parent: EPIC-001
feature: FEATURE-001
priority: P3
status: todo
assignee: ai
created: 2026-10-12
depends-on: []
blocks: []
pr: null
github-issue: null
jira-key: null
---

# `b-modal` renders its title as a `<span>`, so headings inside a dialog follow no heading

## Context

Found by Presenter's accessibility audit (its TASK-025, axe `heading-order`): the help overlay's `<h3>` group
titles follow no `h1`/`h2`, because `b-modal` renders the dialog title as
`<span id="modal-title-…">` (`Birko.Web.Components/src/layout/b-modal.ts:68`). Presenter worked around it locally
(its TASK-032) and recorded the framework change as out of its scope. Any consumer that puts headings inside a
`b-modal` hits the same audit failure.

## Acceptance criteria

- [ ] The title renders as a heading element, `<h2>` by default, with a `heading-level` attribute (1–6) for a dialog
      opened from a deeper context
- [ ] It still labels the dialog through `aria-labelledby`, and looks exactly as it does today (no visual change)
- [ ] A dialog with no `title` renders no empty heading
- [ ] README notes the change: a consumer's own heading inside the dialog now nests under the title

## Out of scope

- `b-drawer` and other overlays: check them while here, and spawn a task if they share the pattern

## Human test plan

N/A — the heading level and the accessible name are asserted by an axe / DOM check in the Playground smoke; the
"looks exactly as today" criterion is covered by its screenshot comparison.
