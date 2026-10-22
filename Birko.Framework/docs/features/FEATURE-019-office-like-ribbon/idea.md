---
id: FEATURE-019
created: 2026-10-22
owner: ai
# status — one of: idea, review (built, sign-off pending), done, dropped, superseded
status: idea
---

# An Office-like ribbon (`b-ribbon`)

> Stakeholder-readable. A project manager or end user should understand the problem and the proposed shape without reading any code.

## Problem

The ribbon at the top of Birko web apps looks and behaves only partly like the Office ribbon people know. Its groups
are not the same height, so the dividers between them stop at different points and the commands do not line up. It
is also missing much of what Office users expect: moving through commands with the arrow keys, buttons that open a
menu of options, a corner button for "more options", tabs that appear only while something is selected, a toolbar
of favourite commands, proper tooltips, on/off buttons, galleries, small inputs, keyboard letters (key tips), and
collapsing by double-click. Every app built on the ribbon inherits these gaps.

## Proposed shape

Fix the layout first: groups of one height, commands in aligned rows, each group's name centred underneath. Then add
the missing Office behaviours one at a time, each as its own piece of work with its own test, and each previewed for
a look decision before it is built. The web ribbon comes first; the desktop (Avalonia) ribbon follows once the web
version has settled.

## Open questions distilled from the grill

No grill interview was run: the user reviewed the ribbon in the browser and answered each item one by one
(2026-10-22). Every item is a decision in [decisions.md](decisions.md).

Questions still to settle inside the work (each belongs to its task):

| id | question | type | blocked-by | state | claimed-by |
|----|----------|------|------------|-------|------------|
| Q1 | Is Ctrl+F1 handled only while the ribbon has focus, or app-wide? | decision | — | open | — |
| Q2 | Which key shows key tips in a browser, where Alt can belong to the browser? | decision | — | open | — |
| Q3 | Does the Quick Access Toolbar sit above or below the tabs by default? | decision | — | open | — |
| Q4 | Are "only one can be on" toggle sets (alignment left/centre/right) in scope? | decision | — | open | — |

**Fog** — concerns not yet precise enough to state as a question:

- How much of this every consumer app wants by default, versus switched on per app.

## Out of scope (initial)

- A "File" backstage view (a full-screen menu behind a File tab).
- The desktop ribbon in this round: filed as later parity work (D13).

## Prototype

Built, then not kept — the layout fix (D1) was previewed live in the Playground on 2026-10-22 by injecting the new
styles into the running ribbon; the user picked the full variant from three. Each later behaviour gets its own look
preview inside its task before it is built.
