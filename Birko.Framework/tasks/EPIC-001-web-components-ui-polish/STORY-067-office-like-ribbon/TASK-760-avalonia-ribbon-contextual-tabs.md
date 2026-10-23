---
id: TASK-760
parent: STORY-067
feature: FEATURE-019
status: done  # signed off by the user 2026-10-23
priority: P3
assignee: ai
created: 2026-10-23
depends-on: [TASK-751]
blocks: []
pr: null
github-issue: null
jira-key: null
---

# Avalonia ribbon: contextual tabs

## Context

FEATURE-019 D13, split from TASK-742. Web counterpart: TASK-734. Desktop counterpart of the web behaviour, with the looks the user picked on the web (D13, decided 2026-10-23: same as web, no new previews). Shared model changes go in `Birko.Xaml.Core/Ribbon`, the control in `Birko.Xaml.Avalonia/Controls`. The narrow-window menu gets the same treatment as the web's mobile list.

`RibbonTab.ContextualSet` with show / hide; the three looks (band, pill, edge) the user kept, in theme colours; hiding the open set returns to the previous normal tab; the set's name is announced with its tabs.

## Acceptance criteria

- [x] Show / hide contextual sets; band / pill / edge, switchable; theme colours
- [x] Hiding the open set returns to the previous normal tab
- [x] Automation names the set; headless tests

## Out of scope

- Anything the web ribbon did not ship

## Human test plan

- [x] Show and hide the Table set in the Gallery in all three looks and every theme

## Outcome (2026-10-23)

- **Sets.** A tab names its set; the app declares the sets (name, theme colour, optionally its own look) and shows or
  hides them. A set's tabs appear after the normal tabs, sets in the order they were declared; a set the app never
  declared never shows. Showing a set can also open its first tab.
- **Going back.** Hiding the set whose tab is open returns to the normal tab that was open before it (or the first
  tab), as on the web. Showing or hiding any other set keeps the open tab open, even when it moves along the strip.
- **The three looks, in theme colours.** Band (a tinted band with the name above its tabs and an accent line), pill
  (the name in a rounded tinted frame before its tabs), edge (an accent line, no name), switchable for the whole
  ribbon or per set. The set's tabs are written in its colour; the open one is underlined. Checked in screenshots in
  all four themes.
- **Assistive technology.** Each contextual tab carries its set's name as help text, so a screen reader reads "Layout,
  Table Tools"; the drawn name is decoration and hidden from it, as the web's.
- **Keyboard and key tips.** Arrows walk from the normal tabs into the sets; Alt letters the shown contextual tabs too.
- **Narrow window.** The ☰ menu lists only the shown tabs.
- **A contract to know.** `SelectedIndex` counts the tabs that are shown, normal ones first. For a ribbon without
  contextual tabs nothing changes.
- **Gate.** New `RibbonContextualTests`, 12 tests:
  - hidden until shown, after the normal tabs, in declaration order; undeclared sets never show;
  - show-and-select, then hide back to the previous normal tab; hiding an earlier set keeps the open tab;
  - band, pill and edge in the theme colour; a set overriding the look; the set named to automation;
  - arrows and key tips reach the shown tabs; the narrow menu; screenshots of the three looks in four themes.
  - Birko.Xaml.Avalonia.Tests 289/289; Birko.Xaml.Core.Tests 60/60; the Gallery builds, with a "A table is
    selected" check box and a look picker.

