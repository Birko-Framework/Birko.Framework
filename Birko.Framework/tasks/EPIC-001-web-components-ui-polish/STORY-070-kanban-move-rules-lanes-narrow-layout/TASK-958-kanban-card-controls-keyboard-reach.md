---
id: TASK-958
parent: STORY-070
feature: FEATURE-022
status: done
priority: P2
assignee: ai
created: 2026-11-22
depends-on: [TASK-946]
blocks: []
pr: null
github-issue: null
jira-key: null
---

# b-kanban: controls on cards keep the board one Tab stop, and the keyboard still reaches them

## Context

Found at TASK-946 (FEATURE-022 D8). Buttons, links and fields that `renderCard` puts on a card are each their own Tab
stop, because the browser puts them in the Tab order. TASK-955 made the board one Tab stop with the arrow keys inside
it; a board whose cards each carry a Retry button now makes Tab walk through every card's button before it leaves the
board. TASK-955's "the board is one tab stop" check passes only because its cards have no controls.

How the keyboard reaches a card's controls instead is D21 in
[FEATURE-022](../../../docs/features/FEATURE-022-kanban-move-rules-lanes-narrow-layout/decisions.md). Enter and Space
already pick a card up (TASK-947/955), which rules out reusing them unless D21 says otherwise.

## Acceptance criteria

- [x] On a board whose cards carry controls, Tab from outside lands on one card, and the Tab order then follows D21 and
      leaves the board without visiting other cards' controls.
- [x] A card's controls can be reached and used by keyboard alone, as D21 decides, including a control inside a
      component's own shadow root (`b-button`).
- [x] Moving between cards with the arrow keys, and picking a card up, work as before.
- [x] A screen-reader user is told that a card has controls and how to reach them.
- [x] Playground checks with real keys: red before, green after.

## Out of scope

- Controls the app adds outside `renderCard` (e.g. in a column header).

## Evidence

Birko.Web `task/TASK-947` 7c0f97f (v0.4.21), Playground e62cf56:
- keyboard-check, real keys in headless Chrome, on a board whose every card has a button and a `b-button`-like control
  (its real button in its own shadow root): 6 TASK-958 checks. Before the change 5 failed: Tab went card A, A's
  controls, then D's controls (every card's buttons were stops), Shift+Tab, arrow-then-Tab and Escape did not follow,
  and no card had the hint. Space on a control already pressed it (TASK-946). After: Tab goes card A > A's Retry > A's
  More > out of the board; Shift+Tab back to the card; → to D then Tab reaches D's Retry; Space presses it and lifts
  nothing; Escape returns to D, where Space picks it up; the card is described "Press Tab to reach its buttons and
  fields." 244/244, three clean runs in a row. Two runs before them each failed different, unrelated ribbon and
  focus-timing checks, which did not recur. My first version of the check pressed Tab once too often (it left the
  board), which made the Shift+Tab checks look red; corrected.
- The TASK-955 checks (one Tab stop, roving focus, the travelling card) keep passing.
- backport-smoke 469/469; a11y-name OK; rerender-leak PASS; entry-points 433/433; locale-keys 23/23; device-fix
  247/247; contextual-ribbon 13/13; Components `tsc` clean. All from builds that printed "✓ Built".

**Signed off** by the user (František Bereň, maintainer) on 2026-11-22, the human test plan below run in the Playground
from the `task/TASK-947` build: "All passed".

## Human test plan

1. In the Playground gallery's kanban, every card carries a Retry button. Tab into the board: focus lands on a card.
   Tab again: that card's Retry. Tab again: focus leaves the board, past no other card's button.
2. Shift+Tab back to the Retry, then Shift+Tab to its card. Press → to move to another card, then Tab: that card's
   Retry. Press Space on it: the Retry toast, the card is not picked up.
3. Press Escape on the Retry: focus returns to its card. Space there picks the card up as before.
4. With a screen reader, a card with a button is announced with a hint that Tab reaches its controls.

## Implementation plan

Drafted 2026-11-22 on the integration branch `task/TASK-947`. Decided (the user): D21.

1. After each render, and whenever the Tab-stop card changes (the board's `focusin`), every focusable element in a
   card's content (a link with `href`, button, input, select, textarea, anything with `tabindex`, and a custom
   element, whose real control sits in its own shadow root) gets `tabindex="-1"` unless its card is the Tab stop; the
   original `tabindex` is kept and put back when the card becomes the Tab stop. The card's own toggle and lane tag stay
   out of the Tab order as before. Nested sub-cards render beside their parent, so each card manages only its own.
2. Escape on a control in a card returns focus to the card (not during a keyboard move, and not when the control
   handled it).
3. A card with controls gets `aria-describedby` to a hidden hint: `bwc.kanban.cardControls`, "Press Tab to reach its
   buttons and fields."
4. README, CHANGELOG, en.json; the gallery kanban puts a Retry button on every card.

**Tests** (red first), keyboard-check with real keys on a board whose every card has a button and a `b-button`-like
shadow control: Tab from outside → card → its two controls → out of the board; Shift+Tab back; → then Tab reaches the
next card's control; Escape on a control → its card; Space on the card still picks it up; the hint is the card's
description. The TASK-955 checks keep passing.
