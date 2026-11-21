---
id: TASK-955
parent: EPIC-001
feature: null
status: verify
priority: P1
assignee: ai
created: 2026-11-21
depends-on: []
blocks: []
findings: [FIELD-056]
pr: null
github-issue: null
jira-key: null
---

# `b-kanban` cards cannot be reached with the keyboard

## Context

Found by the user, 2026-11-21, running [[TASK-947]]'s human test in the Playground: "i cannot do 3" (Tab to a card,
press Space, then →). Every card rendered `tabindex="-1"` and the board `tabindex="0"`, so Tab stopped only on the
board, and the board's key handler does nothing unless a card has focus. With the keyboard alone no card could be
reached, picked up or moved. Birko.Web.Components' ACCESSIBILITY.md already promised a roving tabindex for the kanban.
Pre-existing (not caused by TASK-947); TASK-947's smoke checks focused a card from code and so did not see it.

P1: a whole component unusable without a mouse.

## Change (Birko.Web v0.4.21, on task/TASK-947)

- Roving tabindex: one card carries `tabindex="0"`, the first card, then the card last focused (updated on `focusin`,
  no re-render); the rest `-1`. The board is a tab stop only when it has no cards.
- README keyboard paragraph, CHANGELOG.

## Evidence

Playground `keyboard-check`, trusted key presses: Tab lands on the first card, the next Tab leaves the board, Shift+Tab
returns to the card, returning after ↓ lands on the card last focused, and Space → → Enter moves a card past a locked
column. On the TASK-947 branch before this change 5 of those fail (focus stays on the board); after it 231/231.
verify 0 failing (backport 413/413); a11y-name OK; rerender-leak PASS; entry-points 433/433; locale-keys 23/23;
device-fix 245/245; Components `tsc` clean.

Then the user, retrying: "arrows right dont work". Two more gaps, both pre-existing: without a pick-up, → on a card
without sub-cards did nothing (← already moved focus to the previous column); and during a keyboard move only the live
region changed, so on screen the card looked stuck until Enter. Fixed on the same branch (5400401): → moves focus to
the next column, and a keyboard move highlights the target column with a drop line, as a mouse drag does.
keyboard-check: 2 more checks, red before, 234/234 after; verify 0 failing.

Then again: "still cannot move it right". The move worked, but the card stayed in place until Enter, shown only by a
column tint and a 2px line. The user chose (a): the picked-up card itself travels with the arrows (DOM only; Enter
saves, Escape puts it back). b-kanban 3d22b89 on task/TASK-947; keyboard-check 235/235; checked in Chrome on the gallery
board with real keys (Space, → shows Task A in In progress; the next → is refused at Done; Enter saves).

## Acceptance criteria

- [x] Tab reaches a card, and the board is one tab stop.
- [x] Returning by Tab lands on the card last focused.
- [x] A card can be picked up and moved with the keyboard alone.
- [x] ←/→ move focus between columns, and a keyboard move shows its target on screen.

## Human test plan

1. In the Playground's Data → Kanban, Tab from the page into the board: focus is on a card (visible focus ring).
2. Press ↓, Tab away, Shift+Tab back: the same card has focus.
3. → and ← move focus between columns.
4. Space, then →: the card itself moves into the next column, which is highlighted; another → is refused at Done
   (locked) and said; Enter keeps it there, Escape puts it back.
