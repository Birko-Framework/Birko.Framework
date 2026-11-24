---
id: TASK-976
parent: EPIC-001
feature: null
status: in-progress
priority: P1
assignee: ai
created: 2026-11-24
depends-on: []
blocks: []
related: [TASK-545]
findings: []
pr: null
github-issue: null
jira-key: null
---

# b-toast: the first toast on a page is not announced by a screen reader

## Context

Found on 2026-11-24 running TASK-545's screen-reader step with NVDA 2026.3.1 (portable, speech logged) and Chrome, on
the Playground's `?kb=1` undo-mode page (Birko.Web v0.5.3).

- **The first toast on a page says nothing.** An undo-delete on a freshly loaded page was silent: NVDA spoke no
  "Item deleted", by mouse (focus fell to "document") or by keyboard (only "Undo, button" as focus moved there).
  A second toast on the same page ("Item restored", or "Item deleted" after a warm-up toast) was announced. Chrome's
  accessibility tree shows why: the toast container (`role="status"`, `aria-live="polite"`) does not exist until
  `_ensureContainer` creates it for the first toast, with the toast already inside. Screen readers announce changes to
  a live region that is already in the page, not a region that arrives with its content. So every app's first toast of
  a page load (an error included) is missed: a saved form, a failed request, a deleted row.
- **What is announced runs together.** NVDA spoke "Item deletedUndo ×" and "Item restored×": the message, the action
  button's text and the Dismiss button's visible "×" are flattened into one string with no separation, because both
  buttons sit inside the live region. The Dismiss button is correctly named "Dismiss", but its glyph is still read.
- The item itself is also a live region (`role="status"`/`"alert"` with `aria-live`) nested inside the container's,
  so two regions claim the same text.

## Acceptance criteria

- [ ] The toast container (each position's) exists, empty, as a live region before the first toast is added to it,
      so the first toast is announced. Error toasts stay assertive.
- [ ] A toast is announced as its message alone (for an action toast, the message and the action's name as separate
      words, e.g. "Item deleted. Undo"), without the "×" glyph.
- [ ] One live region per announcement, not a region nested in another.
- [ ] Playground check: the container is in the accessibility tree before the first toast (CDP
      `Accessibility.getFullAXTree`), and the announced text has no "×". Proven red against the current behaviour.
- [ ] NVDA re-run on a fresh page: the first undo-delete says "Item deleted" and that Undo is available.

## Out of scope

- Where focus goes after Undo (TASK-974).
- `ariaNotify`, which `b-sortable-list` uses and NVDA spoke correctly.

## Implementation plan

1. Create each toast container at startup, or the first time the toast manager is used, and append the item on the
   next frame, so the region is in the tree before its content arrives.
2. Mark the close glyph `aria-hidden` (keep its `aria-label` on the button), and give the item's text an explicit
   announcement string, or keep the buttons out of the announced subtree.
3. Drop the item's own `role`/`aria-live` in favour of the container's, keeping assertive for errors (an assertive
   container, or `role="alert"` on error items only).
4. Add the tree check to a Playground smoke; re-run NVDA as in TASK-545.

## Human test plan

1. With NVDA on, load a consumer page with the undo opt-in and delete a row first thing: NVDA says the message and
   that Undo is available, once, without "times" or "×".
2. Trigger an error toast first thing on a fresh page: NVDA announces it.
