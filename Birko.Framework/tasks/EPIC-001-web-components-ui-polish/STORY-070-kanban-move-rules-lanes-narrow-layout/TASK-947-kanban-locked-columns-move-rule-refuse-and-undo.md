---
id: TASK-947
parent: STORY-070
feature: FEATURE-022
status: done
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

- [x] A locked column takes no card and gives none, by mouse or keyboard; picking up a card in it by keyboard is
      refused and announced.
- [x] An optional move rule is asked before every move with the card, where it comes from and where it would go; it is
      asked after the lock.
- [x] A refused place shows that it refuses while a card is dragged over it, and the reason is announced.
- [x] A cancelable `card-move-request` fires before the move is applied; cancelling it keeps the card put and nothing
      else is reported. `card-move` is unchanged (it still fires after the move).
- [x] An undo call puts a card back where its last move took it from, without firing `card-move` or `card-reorder`.
- [x] A board that uses none of this behaves exactly as before (Symbio's three boards do not cancel moves).
- [x] No undo notice is shown by the board (D11).
- [x] Docs (API.md, README) and CHANGELOG; Playground checks red before, green after. (b-kanban has no API.md section;
      its API is documented in the Components README, which now covers all of this.)

## Evidence

Birko.Web `task/TASK-947` b6bc8e4 (v0.4.21), Playground cef3238..402a24a:
- backport-smoke: 13 TASK-947 checks (drag and keyboard into and out of a locked column, the refusal mark and reason,
  `canMove`'s arguments and its reason, `card-move-request` cancelled and not, `revertMove` with and without a move,
  `moveCard()` into a locked column). 12 fail on Birko.Web main; the 13th (`moveCard()` unrestricted) passes before and
  after, as it must. 413/413 with the change, 0 failing checks.
- locale-keys 23/23 (the six `bwc.kanban.*` keys read and present), keyboard 225/225, locale-switch 47/47,
  device-fix 245/245, a11y-name OK, rerender-leak PASS, entry-points 433/433, Components `tsc` clean.
- In the Playground gallery (Chrome): "Done" shows "🔒 Locked" and its card is not draggable.
- Found: the board's own keyboard announcements (pick up, ↑/↓) never reach the live region → TASK-954.

The Playground commit cb55799 did not build (an apostrophe the shell unescaped in a gallery note); 402a24a fixes it,
and the numbers above are from a fresh build.

**Signed off** by the user (František Bereň, maintainer) on 2026-11-22, the human test plan below run in the Playground
from the `task/TASK-947` build: "All passed".

## Human test plan

1. On a board with a locked column, drag a card toward it: the column shows it refuses and says why, and the card
   stays where it was. Try to drag a card out of the locked column: it does not move.
2. With the keyboard, pick up a card in a locked column: it is refused and the reason is read out.
3. In a page that refuses a move (a move rule, or cancelling the move event), try the move: the card never leaves.
4. In a page that undoes a move after a simulated failed save: the card goes back, and the page reports one move only.
5. Open one of Symbio's kanban boards: moves work exactly as before.

## Implementation plan

Drafted 2026-11-21, from `b-kanban.ts` as it stands at Birko.Web v0.4.20.

**Where moves happen today.** A mouse drop (`_onDrop`) and a keyboard drop (`_completeKeyboardDrag`) both call the public
`moveCard()`, which applies the move and then emits `card-move` (another column or parent) or `card-reorder` (same
place). `emit()` dispatches a non-cancelable event. Symbio's three boards read only `detail.card.id`, `fromColumn` and
`toColumn`, then save.

**API (names for review):**
- `KanbanColumn.locked?: boolean` — no card is dragged or keyboard-moved into or out of it by a person.
- `KanbanConfig.canMove?: (move: KanbanMove) => boolean | string` — asked after the lock; `false` refuses, a string
  refuses and is the reason shown and announced. `KanbanMove = { card, fromColumn, toColumn, fromIndex, toIndex,
  fromParent?, toParent? }` (lanes join it in TASK-948).
- Refusing as it happens — see *Open decision* below.
- `revertMove(cardId): boolean` — puts the card back where its last user move took it from; fires no `card-move` /
  `card-reorder`; returns `false` when there is nothing to undo.
- **`moveCard()` stays unrestricted**: it is the app's own call (DraCode's agents move cards into locked columns
  through it). Locks and the rule govern what a *person* can do, by mouse or keyboard. Documented.

**Steps:**
1. One funnel, `_tryUserMove(cardId, target)`, used by the drop and the keyboard drop: source lock, target lock,
   `canMove`, then the refuse-as-it-happens hook; only then `moveCard()`, recording the origin for `revertMove`.
2. Mouse: a card in a locked column is not draggable. `dragover` on a place that refuses does not call
   `preventDefault` (the browser then shows no-drop and fires no drop), marks the column `.drop-refused`, and puts
   the reason in the live region once per target.
3. Keyboard: picking up a card in a locked column is refused and announced; Left/Right skip columns that refuse and
   say which were skipped; Enter on a refused place keeps the card and says why.
4. Look: a locked column shows a lock icon and "Locked" in its header (an accessible name, not colour alone) and a
   `not-allowed` cursor over it while dragging. Plain, no stripes (the mockup's stripes were a sketch).
5. New strings through i18n (`bwc.kanban.locked`, `bwc.kanban.refused`, `bwc.kanban.lockedPickUp`), in `en.json`.
   The board's existing English live-region texts are not translated today — found here, filed separately.
6. Docs: API.md (the new fields, `revertMove`, the funnel rule), CHANGELOG, versions → v0.4.21.

**Tests** (Playground backport-smoke, red before green): a drop and a keyboard move into and out of a locked column
change nothing and fire nothing; `canMove` is asked with the right `KanbanMove` and its `false` / string refuse; the
reason reaches the live region; the refuse hook keeps the card put; `revertMove` restores column and index with no
event; `moveCard()` still moves into a locked column; a board without any of this moves exactly as before
(keyboard-check and contextual-ribbon-check unchanged).

**Decided (the user, 2026-11-21): (B).** A new cancelable `card-move-request`; `card-move` unchanged. The criterion
above is reworded to match. The two options as they were put:
- **(A) `card-move` becomes cancelable and fires before the move is applied** (as this task's criterion is worded).
  One event, but its meaning shifts: `detail.card.columnId` is then still the old column while the event runs, and
  the board reads the old position. Symbio's three handlers do not read either, so nothing breaks today.
- **(B) a new cancelable `card-move-request` fires before; `card-move` stays exactly as it is, after the move.**
  Nothing changes for existing listeners; one more event to learn.
