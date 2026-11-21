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

## Acceptance criteria

- [x] Tab reaches a card, and the board is one tab stop.
- [x] Returning by Tab lands on the card last focused.
- [x] A card can be picked up and moved with the keyboard alone.

## Human test plan

1. In the Playground's Data → Kanban, Tab from the page into the board: focus is on a card (visible focus ring).
2. Press ↓, Tab away, Shift+Tab back: the same card has focus.
3. Space, → →, Enter: the card moves, and Done (locked) is skipped.
