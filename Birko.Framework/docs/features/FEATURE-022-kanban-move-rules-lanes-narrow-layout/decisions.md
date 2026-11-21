---
id: FEATURE-022
created: 2026-11-21
---

# Kanban board: move rules, lanes, a narrow layout, and buttons on cards — Decisions

> The decision ledger for stakeholders. Every idea-branch is a row with exactly one **state**. Rows are never deleted — `removed` is a state, not a deletion — so the ledger stays auditable.

## Decisions

| ID | Decision | State | Rationale | Date | By | → Tasks |
|----|----------|-------|-----------|------|----|---------|
| D1 | A column can be locked: no card is dragged or keyboard-moved into it or out of it | approved | Chosen in the grill: the common case (DraCode's In progress, Blocked, Done) needs no code from the app | 2026-11-21 | František Bereň (maintainer) | TASK-947 |
| D2 | An app can also supply its own move rule, asked before every move; a lock is checked first | approved | Chosen in the grill with D1, over a rule alone (a read-only column would then need code) and a lock alone (cannot say "only between these lanes") | 2026-11-21 | František Bereň (maintainer) | TASK-947 |
| D3 | An app can refuse a move as it happens; the card then stays where it was | approved | Chosen in the grill. Today a move is reported only after it has happened. Pages that do not refuse (all of Symbio's today) see no difference | 2026-11-21 | František Bereň (maintainer) | TASK-947 |
| D4 | An app can undo a move after the fact, for example when saving it failed, and the undo is not reported as a new move | approved | Chosen in the grill: DraCode needs "a failed priority change puts the card back and shows an error"; undoing by moving the card back today reports a second move the app has to ignore | 2026-11-21 | František Bereň (maintainer) | TASK-947 |
| D5 | Lanes, one level deep, each with its own label and card count and each a place to drop a card; an empty lane stays visible; a move reports the lane a card left and the lane it entered. The page chooses how lanes are shown: as stacked sections inside the column (the default), as rows across the whole board (swimlanes), or as a tag on each card | changed | Chosen in the grill over columns inside columns. Changed 2026-11-21 when TASK-948 was planned: the maintainer asked for "all 3 as option" (sections, rows across the board, a tag on the card), with sections the default | 2026-11-21 | František Bereň (maintainer) | TASK-948, TASK-956, TASK-957 |
| D6 | A board stacks its columns one under another, with no sideways scrolling, only when the page asks for it and only when the board itself is narrower than the width the page gives; lanes stay grouped under their headings when stacked | approved | Chosen in the grill over stacking every board automatically, which would change Symbio's three boards unasked. Measured on the board's own width, so a board beside a docked chat stacks too | 2026-11-21 | František Bereň (maintainer) | TASK-950 |
| D7 | A column can turn reordering off: a card dropped there takes the place the app gives it, and dragging within it does nothing | approved | Chosen in the grill over one switch for the whole board and over refusing it through the move rule (the board would still show a drop position it will not keep). Columns keep reordering unless they turn it off | 2026-11-21 | František Bereň (maintainer) | TASK-949 |
| D8 | Buttons, links and form fields on a card, and anything the app marks, do not start a drag and do not count as a click on the card | approved | Chosen in the grill over exempting only what the app marks. To be checked when built: Symbio's cards must not rely on such an element counting as a card click | 2026-11-21 | František Bereň (maintainer) | TASK-946 |
| D9 | With the keyboard, Left and Right move a picked-up card between columns as now; Up and Down move it within a lane and on into the lane above or below (straight to the next lane where reordering is off). Locked columns and refused places are skipped, and the screen reader names the lane | approved | Chosen in the grill over stepping through lanes with Left and Right, which would not match lanes stacked on screen | 2026-11-21 | František Bereň (maintainer) | TASK-949 |
| D10 | Build a clickable mockup before any code: a DraCode-like board with locked columns, a To do column with four lanes, a card with a Retry button, and a width slider showing the stacked layout | approved | Chosen in the grill: lanes, locked columns and the narrow layout are all things to look at before building | 2026-11-21 | František Bereň (maintainer) | — (the mockup: built and deleted) |
| D11 | After a successful move the board shows no undo notice of its own; whether to offer one is the app's choice, using the undo that D4 provides | approved | Asked at decompose, after the mockup raised it under D4: keeps the board free of a second notification system next to the app's own; DraCode can show one with its existing notice | 2026-11-21 | František Bereň (maintainer) | TASK-947 |
| D12 | A lane can be collapsed, or given a card limit, like a column | deferred | Not now: DraCode's four priority lanes need neither. Revisit when an app asks for a collapsible or limited lane | 2026-11-21 | František Bereň (maintainer) | — |
| D13 | A card moved to another lane of the same column is reported as a move (card-move), with the lane it left and the lane it entered, as a column change is | approved | Asked when TASK-948 was planned: for DraCode a lane change is a priority change to save, like a column change | 2026-11-21 | František Bereň (maintainer) | TASK-948 |
| D14 | In a column with lanes, a card that names no lane, or a lane the column does not have, shows in the column's default lane (the one the page names, else the first), so no card disappears | approved | Asked when TASK-948 was planned, over hiding such a card until it names a real lane | 2026-11-21 | František Bereň (maintainer) | TASK-948 |

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
- 2026-11-21 — reaction to the mockup, bearing on D1 and D2, from the maintainer: "i cennot do wsitching between lanes
  since they ar e locked", then "i meant between columns". The mockup only showed DraCode's setup, where every column
  but To do is locked and DraCode's own rule allows moves only between To-do lanes, so no card could change column.
  The mockup gained a switch that turns DraCode's setup off: every column then takes cards by mouse and keyboard, and
  a card dropped on a column with lanes lands in its Normal lane. A refused drop now shows its reason while the card
  hovers, since a browser fires no drop on a refused place. No row changed.
- 2026-11-21 — D1–D10 proposed → approved, by the maintainer after trying the mockup: "all approved, grouped lanes,
  empty lanes stay". Two of the mockup's questions were answered into the rows: D5 now says an empty lane stays visible
  as a place to drop, and D6 that lanes stay grouped under their headings on a stacked board (the one-list form is not
  built). The third, whether a card offers an undo after a successful move, was not answered: D4 keeps its approved
  form (refuse before, undo after a failed save), and an undo after success stays the app's own choice.
- 2026-11-21 — the mockup has answered every row it was built for and is deleted (last version at a0785383).
- 2026-11-21 — D11 added and approved, D12 added and deferred, by the maintainer at decompose: the undo-after-success
  question the mockup raised under D4 (unanswered at decide), and the lane-collapse and lane-limit question from
  idea.md's fog list.
- 2026-11-21 — decomposed under STORY-070 (EPIC-001), five tasks, the split chosen by the maintainer:
  D8 → TASK-946; D1, D2, D3, D4, D11 → TASK-947; D5 → TASK-948; D7, D9 → TASK-949; D6 → TASK-950. D10 needs no task
  (the mockup was built and deleted); D12 is deferred.
- 2026-11-21 — D5 approved → changed, by the maintainer while TASK-948 was planned: "all 3 as option". Lanes can be
  shown as sections inside a column (the default, as decided and mocked), as rows across the whole board, or as a tag
  on each card; the page chooses. Sections stay TASK-948; rows → TASK-956, tags → TASK-957, all in the one v0.4.21
  release. Open for TASK-956's plan: rows across the board need the same lanes in every column, while D5's lanes
  belong to one column. D6 (lanes stay grouped when stacked) was decided for sections; how rows and tags stack is for
  their tasks.
- 2026-11-21 — D13 and D14 added and approved, by the maintainer at TASK-948's plan (the lane-change event; a card
  with no or an unknown lane).
