---
id: TASK-947
parent: STORY-070
feature: FEATURE-022
status: todo
priority: P2
assignee: ai
created: 2026-11-21
depends-on: []
blocks: [TASK-948]
pr: null
github-issue: null
jira-key: null
---

# b-kanban: locked columns, a move rule, and refusing or undoing a move

## Context

FEATURE-022 D1, D2, D3, D4 and D11 (approved by the user 2026-11-21):
- D1: a column can be locked: no card is dragged or keyboard-moved into it or out of it.
- D2: an app can also supply its own move rule, asked before every move; a lock is checked first.
- D3: an app can refuse a move as it happens; the card then stays where it was.
- D4: an app can undo a move after the fact (a failed save), and the undo is not reported as a new move.
- D11: after a successful move the board shows no undo notice of its own.

Today `disabled` is all or nothing, and `card-move` fires after the card has moved and cannot be cancelled. The
mockup (deleted; last version at a0785383) showed a refused drop's reason while the card hovers, because a browser
fires no drop on a place that refused it.

Decisions: [FEATURE-022](../../../docs/features/FEATURE-022-kanban-move-rules-lanes-narrow-layout/decisions.md).

## Acceptance criteria

- [ ] A locked column takes no card and gives none, by mouse or keyboard; picking up a card in it by keyboard is
      refused and announced.
- [ ] An optional move rule is asked before every move with the card, where it comes from and where it would go; it is
      asked after the lock.
- [ ] A refused place shows that it refuses while a card is dragged over it, and the reason is announced.
- [ ] `card-move` can be cancelled before the move is applied; the card stays put and nothing else is reported.
- [ ] An undo call puts a card back where its last move took it from, without firing `card-move` or `card-reorder`.
- [ ] A board that uses none of this behaves exactly as before (Symbio's three boards do not cancel moves).
- [ ] No undo notice is shown by the board (D11).
- [ ] Docs (API.md, README) and CHANGELOG; Playground checks red before, green after.

## Human test plan

1. On a board with a locked column, drag a card toward it: the column shows it refuses and says why, and the card
   stays where it was. Try to drag a card out of the locked column: it does not move.
2. With the keyboard, pick up a card in a locked column: it is refused and the reason is read out.
3. In a page that refuses a move (a move rule, or cancelling the move event), try the move: the card never leaves.
4. In a page that undoes a move after a simulated failed save: the card goes back, and the page reports one move only.
5. Open one of Symbio's kanban boards: moves work exactly as before.
