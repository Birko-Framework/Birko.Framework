---
id: TASK-976
parent: EPIC-001
feature: null
status: done
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

- [x] A live region exists, empty, before the first toast, so the first toast is announced; error toasts are
      assertive. (Built as two announcers the toast manager adds at startup, `#b-toast-announce` polite and
      `#b-toast-announce-alert` assertive, rather than making the visible container the live region: the container
      holds the buttons, which is what ran into the message.)
- [x] A toast is announced as its message; an action toast as "Item deleted. Undo is available."
      (`bwc.toast.actionAvailable`), without the "×" glyph (also `aria-hidden` on the close button).
- [x] One live region per announcement: the container and the toast item are no longer live regions.
- [x] Playground check (`toast-announce-check.mjs`, CDP accessibility tree): the announcers are live regions in the
      tree before any toast, the first toast is said once without its buttons, an error goes to the assertive one, and
      the announcer follows a modal. 0/7 on Birko.Web main v0.5.3, 7/7 with the fix.
- [x] NVDA re-run on a fresh page: the first undo-delete said "Item deleted. Undo is available.", and Undo then
      "Item restored".

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

## Evidence

Birko.Web `task/TASK-976` 4057a32 (joins the unpushed v0.5.3), Playground 9a62322. All from builds that printed
"✓ Built".
- `toast-announce-check` 7/7 (0/7 against main v0.5.3); verify 0 failing (undo-delete-smoke 42/42, backport 492/492;
  backport's TASK-133 focusField check failed once and passed on the rerun); keyboard-check 244/244 (one earlier run
  243/244, passed on the rerun); a11y-name OK (43); locale-keys 23/23; locale-switch 47/47; escape-in-modal 27/27;
  entry-points 433/433; rerender-leak PASS; Components `tsc` clean.
- NVDA 2026.3.1 (portable, its speech log read), Chrome in front, fresh `?kb=1` page with no toast shown yet: a mouse
  delete said "Item deleted. Undo is available."; a keyboard delete the same, and Undo then "Item restored".

## Human test plan

1. [x] With NVDA on, load a consumer page with the undo opt-in and delete a row first thing: NVDA says the message and
   that Undo is available, once, without "times" or "×".
2. [x] Trigger an error toast first thing on a fresh page: NVDA announces it.

**Passed 2026-11-24** by the user with a screen reader, on two temporary Playground pages made for it (a fresh
undo-mode list page, and a fresh page whose first toast is an error); the pages were removed before the merge.

**Signed off** by the user (František Bereň, maintainer) on 2026-11-24: "all passed". Merged into Birko.Web main
83085d1 as part of v0.5.3 (the unpushed tag moved to the merge); `toast-announce-check` 7/7 on main.
