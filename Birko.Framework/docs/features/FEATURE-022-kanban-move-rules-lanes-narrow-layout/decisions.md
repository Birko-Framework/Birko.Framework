---
id: FEATURE-022
created: 2026-11-21
---

# Kanban board: move rules, lanes, a narrow layout, and buttons on cards — Decisions

> The decision ledger for stakeholders. Every idea-branch is a row with exactly one **state**. Rows are never deleted — `removed` is a state, not a deletion — so the ledger stays auditable.

## Decisions

| ID | Decision | State | Rationale | Date | By | → Tasks |
|----|----------|-------|-----------|------|----|---------|
| D1 | A column can be locked: no card is dragged or keyboard-moved into it or out of it | proposed | Chosen in the grill: the common case (DraCode's In progress, Blocked, Done) needs no code from the app | — | — | — |
| D2 | An app can also supply its own move rule, asked before every move; a lock is checked first | proposed | Chosen in the grill with D1, over a rule alone (a read-only column would then need code) and a lock alone (cannot say "only between these lanes") | — | — | — |
| D3 | An app can refuse a move as it happens; the card then stays where it was | proposed | Chosen in the grill. Today a move is reported only after it has happened. Pages that do not refuse (all of Symbio's today) see no difference | — | — | — |
| D4 | An app can undo a move after the fact, for example when saving it failed, and the undo is not reported as a new move | proposed | Chosen in the grill: DraCode needs "a failed priority change puts the card back and shows an error"; undoing by moving the card back today reports a second move the app has to ignore | — | — | — |
| D5 | A column can hold lanes, one level deep, each with its own label and card count and each a place to drop a card; a move reports the lane a card left and the lane it entered | proposed | Chosen in the grill over columns inside columns, which would make every column rule (lock, count, collapse, keyboard) work at two levels | — | — | — |
| D6 | A board stacks its columns one under another, with no sideways scrolling, only when the page asks for it and only when the board itself is narrower than the width the page gives | proposed | Chosen in the grill over stacking every board automatically, which would change Symbio's three boards unasked. Measured on the board's own width, so a board beside a docked chat stacks too | — | — | — |
| D7 | A column can turn reordering off: a card dropped there takes the place the app gives it, and dragging within it does nothing | proposed | Chosen in the grill over one switch for the whole board and over refusing it through the move rule (the board would still show a drop position it will not keep). Columns keep reordering unless they turn it off | — | — | — |
| D8 | Buttons, links and form fields on a card, and anything the app marks, do not start a drag and do not count as a click on the card | proposed | Chosen in the grill over exempting only what the app marks. To be checked when built: Symbio's cards must not rely on such an element counting as a card click | — | — | — |
| D9 | With the keyboard, Left and Right move a picked-up card between columns as now; Up and Down move it within a lane and on into the lane above or below (straight to the next lane where reordering is off). Locked columns and refused places are skipped, and the screen reader names the lane | proposed | Chosen in the grill over stepping through lanes with Left and Right, which would not match lanes stacked on screen | — | — | — |
| D10 | Build a clickable mockup before any code: a DraCode-like board with locked columns, a To do column with four lanes, a card with a Retry button, and a width slider showing the stacked layout | proposed | Chosen in the grill: lanes, locked columns and the narrow layout are all things to look at before building | — | — | — |

_`Date` and `By` stay `—` until `/feature decide` stamps a verdict — they record **when/who decided**, not when the row was created (creation is in the History log)._

**States:** `proposed` (fresh from grill, awaiting decision) · `approved` (build it) · `deferred` (not now — note unblock condition) · `changed` (approved but altered — record the delta) · `removed` (rejected / out of scope).

Only `approved` and `changed` rows generate tasks at `/feature decompose`. No row is terminal: a `deferred`/`removed` decision overturned by later evidence (incl. production feedback) is **reopened** by adding a *new* `proposed` row that links the superseded one — the old row is never deleted.

## History log

> Append-only. Every state change gets a dated line with the reason — this is the "why it changed", not just the current value.

- 2026-11-21 — feature created from DraCode's request (session dracode-aa, for its Board, its tasks 153 and 154);
  decisions seeded as `proposed` from the grill-me interview, asked in the session's picker. The request and the user's
  go-ahead are in docs/BRIEF.md under 2026-11-21.
- 2026-11-21 — clickable mockup built (prototype.html, an HTML mockup). Building it raised three questions, asked on the
  page and left for `/feature decide`: under D4, whether a card should also offer an undo after a successful move or
  leave that to the app; under D5, whether an empty lane stays visible or collapses; under D6, whether lanes on a
  stacked narrow board stay grouped under their headings or become one list with the lane on each card (the page
  shows both). No row added; all ten stay `proposed`.
