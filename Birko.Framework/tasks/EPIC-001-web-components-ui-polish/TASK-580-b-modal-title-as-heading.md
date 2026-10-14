---
id: TASK-580
parent: EPIC-001
feature: FEATURE-001
priority: P3
status: done
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

- [x] The title renders as a heading element, `<h2>` by default, with a `heading-level` attribute (1–6) for a dialog
      opened from a deeper context
- [x] It still labels the dialog through `aria-labelledby`, and looks exactly as it does today (no visual change)
- [x] A dialog with no `title` renders no empty heading
- [x] The title is rendered as text, not HTML: `render()` interpolates `${title}` raw today, so a title carrying a
      server value or tenant name with markup runs in the page — the same defect TASK-597 fixed in `b-toast`
      (noted 2026-10-14, while re-reading line 68 for this task)
- [x] README notes the change: a consumer's own heading inside the dialog now nests under the title

## Out of scope

- `b-drawer` and other overlays: check them while here, and spawn a task if they share the pattern. **Checked:**
  `b-drawer` shares both halves and has no accessible name at all, spawned as [[TASK-609]] (P2, for the escaping);
  `b-confirm-dialog` already escapes its title and labels its dialog.

## Human test plan

N/A — the heading level and the accessible name are asserted by an axe / DOM check in the Playground smoke; the
"looks exactly as today" criterion is covered by its screenshot comparison.

## Progress

**2026-10-14 — done, released in Birko.Web v0.1.9** (Web `b78e148`, Playground `1ce86b6`).

- The title is `<h{level} class="overlay-title">`, `heading-level` 1–6 (anything else → 2), `margin: 0; font: inherit`
  so it keeps the header bar's type. No title renders an empty `<span>` (keeps the Close button right-aligned) and no
  `aria-labelledby`, rather than one pointing at an empty node.
- **The escaping criterion was not hypothetical:** the red run showed an `<img onerror>` title executing
  (`__modalTitlePwned = 1`).
- Gate: `modal-title-smoke`, 24 checks; **red 3/19** before the fix (the count grows once a heading exists to measure),
  24/24 after; full `verify.mjs` green. "Looks as today" is asserted as computed font-size / weight / family /
  line-height / letter-spacing equal to the header's and zero margin, in place of a screenshot comparison.

