---
id: TASK-761
parent: STORY-067
feature: FEATURE-019
status: done  # signed off by the user 2026-10-23
priority: P3
assignee: ai
created: 2026-10-23
depends-on: [TASK-752]
blocks: []
pr: null
github-issue: null
jira-key: null
---

# Avalonia ribbon: galleries

## Context

FEATURE-019 D13, split from TASK-742. Web counterpart: TASK-740. Desktop counterpart of the web behaviour, with the looks the user picked on the web (D13, decided 2026-10-23: same as web, no new previews). Shared model changes go in `Birko.Xaml.Core/Ribbon`, the control in `Birko.Xaml.Avalonia/Controls`. The narrow-window menu gets the same treatment as the web's mobile list.

`RibbonItem.Gallery`: a row of visual choices with a full grid behind it, in the three looks the user kept (framed, open, dropdown); picking selects and reports; ▲ ▼ step a row; the grid works by keyboard; two tiles in a small group; the grid opens over a folded group's popup.

## Acceptance criteria

- [x] The three looks, switchable; picking selects and reports; set / get the selection
- [x] Row stepping with disabled ends; the grid by arrows, Enter, Escape; key tips
- [x] Small and folded groups; the narrow menu lists the choices; headless tests

## Out of scope

- Anything the web ribbon did not ship

## Human test plan

- [x] Scroll, expand and pick from the Styles gallery by mouse and keyboard, wide and narrow

## Outcome (2026-10-23)

- **The three looks.** Framed (Office: a framed row of tiles with ▲ ▼ ▾ stacked beside it), open (‹ › around the
  row, "More ▾" under it) and dropdown (only the current choice, opening the grid), for the whole ribbon or per
  gallery. Tiles show a large preview over the name, at the web's tile size.
- **Picking.** A tile picks the choice: it is marked with the toggle's pressed look (primary tint and frame, the same
  size), its command runs, and the ribbon reports the pick with the gallery as parent. The app can set or clear the
  choice without an event, and read it.
- **Stepping.** ▲ ▼ (or ‹ ›) move the row by a row's worth; the last row is a full one; the end buttons are disabled.
  Focus stays on the button, or moves to the other one when its end is reached.
- **The grid.** ▾ opens every choice in the declared number of columns, over the gallery. Focus starts on the current
  choice; arrows move by one and by a row, Home and End to the ends, Enter picks, Escape closes back to ▾.
- **Small and folded groups.** A small group shows two tiles. In a folded group's popup the gallery works the same,
  and its grid opens over the popup; a pick closes both, as a command does.
- **In place.** Picking or stepping redraws the gallery where it is, so an unpinned ribbon's temporary reveal stays
  open while the user steps through it.
- **Key tips and the narrow menu.** Alt letters the tiles (a declared letter first) and ▾, whose letter opens the grid
  with letters on every tile. The ☰ menu lists the choices as radio entries, the current one checked.
- **Gate.** New `RibbonGalleryTests`, 13 tests:
  - stepping with disabled ends; a pick selects, runs and reports; the app sets and clears the choice silently;
  - the picked look in tokens and the same size; the open and dropdown looks;
  - the grid by keyboard (columns, arrows, Escape, End and Enter, focus after the pick);
  - a small group; a folded group's popup with the grid over it; stepping inside an unpinned reveal;
  - the narrow menu; key tips through to the grid; screenshots of the three looks and the grid.
  - Birko.Xaml.Avalonia.Tests 302/302; Birko.Xaml.Core.Tests 60/60; the Gallery builds, with a Styles gallery and a
    look picker.
- **Seen in the screenshots, not changed here.** Group names at the bottom of the row look cut at the baseline in the
  headless screenshots, in this task's and in the earlier layout ones alike; worth a look in the real Gallery.

