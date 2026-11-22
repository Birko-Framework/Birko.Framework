---
id: TASK-946
parent: STORY-070
feature: FEATURE-022
status: done
priority: P2
assignee: ai
created: 2026-11-21
depends-on: []
blocks: []
pr: null
github-issue: null
jira-key: null
---

# b-kanban: buttons, links and fields on a card do not start a drag or count as a card click

## Context

FEATURE-022 D8 (approved by the user 2026-11-21): buttons, links and form fields on a card, and anything the app
marks, do not start a drag and do not count as a click on the card. Asked for by DraCode, whose failed cards carry a
Retry button rendered through `renderCard`.

Today a mousedown anywhere on a card can start a drag, and a click anywhere fires `card-click`.

Decisions: [FEATURE-022](../../../docs/features/FEATURE-022-kanban-move-rules-lanes-narrow-layout/decisions.md).

## Acceptance criteria

- [x] A click on a `button`, `a`, `input`, `select`, `textarea` or `label` inside a card, or on anything inside an
      element marked `data-no-drag`, fires no `card-click` and starts no drag (mouse and touch).
- [x] Keyboard: Space or Enter on such a control acts on the control, not as a pick-up of the card.
- [x] A click elsewhere on the card still fires `card-click`, and dragging from elsewhere still works.
- [x] Checked before shipping: none of Symbio's three boards (manufacturing orders, kitchen, tasks) relies on a click
      on such an element counting as a card click (record what was found).
      **Found (2026-11-22):** none puts a button, link, field, label or `data-no-drag` element in a card. The
      manufacturing-orders and tasks cards hold text and a `b-badge`, the kitchen card text and a list; their
      `card-click` handlers lose nothing.
- [x] Playground check: red before, green after.

## Evidence

Birko.Web `task/TASK-947` fde5210 (v0.4.21), Playground 44d4804:
- backport-smoke: 5 TASK-946 checks over 8 kinds of control on a card (button, link, input, select, textarea, label,
  inside `[data-no-drag]`, and a button in a component's own shadow root, as in `b-button`). Before the change 3
  failed: a click on every one was a card click, the title click was counted among them, and a drag began from every
  one. The other 2 held already: after a press on a control, a drag from the title still moves the card, and Space on
  a focused control picks nothing up. After: 464/464, 0 failing checks.
- A first version found the control with `closest()` and missed the shadow-root button: 3 checks red on it, green
  once the event's composed path is read up to the card. A test artefact on the way: a synthetic `dragstart` must be
  `composed`, as a browser's own is, or it never leaves the shadow root.
- keyboard-check, real keys and a real mouse in headless Chrome: Space, Enter and a click on a card's button press the
  button (3) and count as no card click. Before the change: 3 card clicks. After: 238/238.
- verify 0 failing; a11y-name OK; rerender-leak PASS; entry-points 433/433; locale-keys 23/23; device-fix 247/247;
  contextual-ribbon 13/13; Components `tsc` clean. All from builds that printed "✓ Built".
- The gallery kanban's Task B has a Retry button: pressing it shows a "Retry pressed" toast; the rest of the card
  shows a "Card click" toast.

**Signed off** by the user (František Bereň, maintainer) on 2026-11-22, the human test plan below run in the Playground
from the `task/TASK-947` build: "All passed".

## Human test plan

1. In the Playground gallery's kanban, Task B has a Retry button. Click it: a "Retry pressed" toast, no "Card click"
   toast, and the card does not move.
2. Try to drag the card by the button: nothing happens. Drag it by its title: it moves as before.
3. Tab to the button and press Space: the button acts; the card is not picked up.
