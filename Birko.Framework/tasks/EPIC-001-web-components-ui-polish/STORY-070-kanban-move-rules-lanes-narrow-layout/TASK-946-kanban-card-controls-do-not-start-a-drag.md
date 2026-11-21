---
id: TASK-946
parent: STORY-070
feature: FEATURE-022
status: todo
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

- [ ] A click on a `button`, `a`, `input`, `select`, `textarea` or `label` inside a card, or on anything inside an
      element marked `data-no-drag`, fires no `card-click` and starts no drag (mouse and touch).
- [ ] Keyboard: Space or Enter on such a control acts on the control, not as a pick-up of the card.
- [ ] A click elsewhere on the card still fires `card-click`, and dragging from elsewhere still works.
- [ ] Checked before shipping: none of Symbio's three boards (manufacturing orders, kitchen, tasks) relies on a click
      on such an element counting as a card click (record what was found).
- [ ] Playground check: red before, green after.

## Human test plan

1. In the Playground's kanban, give a card a button (via `renderCard`). Click it: no card click is reported and the
   card does not move.
2. Try to drag the card by the button: nothing happens. Drag it by its title: it moves as before.
3. Tab to the button and press Space: the button acts; the card is not picked up.
