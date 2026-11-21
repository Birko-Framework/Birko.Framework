---
id: FEATURE-022
created: 2026-11-21
owner: both
# status — one of: idea, review (built, sign-off pending), done, dropped, superseded
status: idea
---

# Kanban board: move rules, lanes, a narrow layout, and buttons on cards

> Stakeholder-readable. A project manager or end user should understand the problem and the proposed shape without reading any code.

## Problem

Birko's kanban board (the `b-kanban` web component) lets a person drag cards between columns. DraCode wants its project
Board built on it, and today it cannot be, so DraCode builds its own columns by hand. Four things are missing:

- **Some columns must not take or give cards.** On DraCode's Board the agents move cards; a person must not drag a card
  into "In progress", "Blocked" or "Done", or out of them. The board today is either fully draggable or not at all.
  DraCode also needs to refuse a move, or put a card back when saving it fails ("a failed priority change puts the
  card back and shows an error"). Today the board reports a move only after it has happened, and cannot be told no.
- **Lanes inside a column.** DraCode's next step splits "To do" into priority lanes (Critical, High, Normal, Low); moving
  a card to another lane changes its priority. The board has no lanes.
- **A narrow board scrolls sideways.** On a phone, or beside a docked chat, the columns keep their width and the board
  scrolls left and right. DraCode needs the columns stacked one under another instead.
- **A button on a card starts a drag.** DraCode's failed cards carry a Retry button; clicking it must not pick the card
  up or count as clicking the card.

Symbio already uses this board on three pages (manufacturing orders, the restaurant kitchen, tasks). Nothing there may
change unless a page asks for the new behaviour.

## Proposed shape

- A column can be **locked**: no card goes into it or out of it, by mouse or by keyboard. For rules a lock cannot
  express ("only between these lanes"), the app can supply its own **move rule**, asked before every move.
- The app can **refuse a move** before it happens, and **undo a move** after the fact (when saving it failed) without the
  board reporting a second move.
- A column can hold **lanes**, stacked inside it, each with its own label and card count and each a place to drop a card.
  A move reports which lane a card came from and went to.
- A board can ask to **stack its columns** when it is narrower than a given width, with no sideways scrolling. Without
  that request it looks as it does today.
- A column can turn **reordering off**: a card dropped there takes the place the app gives it, and dragging within it
  does nothing.
- Buttons, links and form fields on a card, and anything the app marks, **do not start a drag** and do not count as a
  click on the card.
- With the **keyboard**, Left and Right still move a picked-up card between columns; Up and Down move it within a lane and
  on into the lane above or below. Locked columns and refused places are skipped, and the screen reader names the lane.

## Open questions distilled from the grill

The grill resolved every question it raised — see decisions.md.

| id | question | type | blocked-by | state | claimed-by |
|----|----------|------|------------|-------|------------|
| Q1 | How does an app say which moves are allowed? | decision | — | resolved → D1, D2 | — |
| Q2 | How does an app refuse a move, or put a card back after a failed save? | decision | Q1 | resolved → D3, D4 | — |
| Q3 | How are lanes inside a column described? | decision | — | resolved → D5 | — |
| Q4 | Does the board stack automatically when narrow, or only when asked? | decision | — | resolved → D6 | — |
| Q5 | How does an app turn reordering off? | decision | Q3 | resolved → D7 | — |
| Q6 | Which parts of a card do not start a drag? | decision | — | resolved → D8 | — |
| Q7 | How do lanes work with the keyboard? | decision | Q1, Q3 | resolved → D9 | — |
| Q8 | Prototype before building? | decision | — | resolved → D10 | — |

**Fog** — concerns not yet precise enough to state as a question:

- None left: lane collapse and lane limits became D12 (deferred).

## Out of scope (initial)

- Lanes inside lanes. Lanes are one level inside a column.
- Moving DraCode's Board onto the component, and its priority lanes: DraCode's own work (its tasks 153 and 154), once
  this ships.
- Changing how Symbio's three boards look or behave; each can opt into the new behaviour later.

## Prototype

Built, then deleted — answered D1, D2, D3, D4, D5, D6, D7, D8, D9, D10; last version at a0785383
