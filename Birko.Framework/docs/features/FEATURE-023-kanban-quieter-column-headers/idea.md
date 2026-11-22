---
id: FEATURE-023
created: 2026-11-22
owner: both
# status — one of: idea, review (built, sign-off pending), done, dropped, superseded
status: idea
---

# Kanban board: quieter column headers (footer, lock, a board-wide lock)

> Stakeholder-readable. A project manager or end user should understand the problem and the proposed shape without reading any code.

## Problem

Asked for by DraCode (2026-11-22), after running its project board on Birko.Web v0.4.21 (FEATURE-022). On a board
where every column is locked, each column header shows a "Locked" chip, so it repeats four times, and each column has
a footer saying "N cards", which repeats the count already in the header.

DraCode's request, as sent: "a way to hide the per-column footer ("N cards" repeats the header count) and to show the
lock as just the icon (with an accessible label) instead of the "Locked" chip in every header. When the whole board is
locked, the chip repeats four times. A board-level `locked` might suit that case."

Screenshots from DraCode's board: wide, light, all columns locked (chip in four headers, the footers).

## Proposed shape

To be grilled. Three asks: the page can hide the column footer; the lock can show as an icon only, still named for
screen readers; a whole board can be locked in one place, perhaps shown once instead of per column.

## Open questions

| # | Question | State |
|---|---|---|
| Q1 | Is hiding the footer a page option, or should the footer go by default (it repeats the header count)? | open |
| Q2 | Icon-only lock: an option, or the new default? | open |
| Q3 | A board-wide lock: one setting that locks every column? How is it shown: once for the board, or per column? | open |

## Out of scope (initial)

- Changing what a lock does (FEATURE-022 D1-D4).

## Prototype

Not built yet: a header mockup (chip, icon, once-per-board) would let DraCode pick.
