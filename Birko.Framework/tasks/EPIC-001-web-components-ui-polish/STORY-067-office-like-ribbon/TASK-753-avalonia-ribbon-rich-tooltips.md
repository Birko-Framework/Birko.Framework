---
id: TASK-753
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

# Avalonia ribbon: rich tooltips (name, description, shortcut)

## Context

FEATURE-019 D13, split from TASK-742. Web counterpart: TASK-738. Desktop counterpart of the web behaviour, with the looks the user picked on the web (D13, decided 2026-10-23: same as web, no new previews). Shared model changes go in `Birko.Xaml.Core/Ribbon`, the control in `Birko.Xaml.Avalonia/Controls`. The narrow-window menu gets the same treatment as the web's mobile list.

`RibbonItem.Description` and `Shortcut`; the tooltip shows the name, the description under it and the shortcut, in the light tooltip look; it shows on hover after a delay and at once on keyboard focus; Escape hides it first; the shortcut is exposed to automation.

## Acceptance criteria

- [x] `RibbonItem.Description` / `Shortcut` in Birko.Xaml.Core
- [x] Tooltip with name, description and shortcut in the light look; hover delay, at once on keyboard focus, Escape hides it
- [x] Automation name and help text set; headless tests

## Out of scope

- Anything the web ribbon did not ship

## Human test plan

- [x] Hover and keyboard-focus commands in the Gallery: the tooltip shows name, description and shortcut, and Escape hides it

## Outcome (2026-10-23)

- **Before.** Only a Small command and a folded group had a tooltip: the name as a plain string, in the dark tooltip
  look, shown on hover only.
- **Model.** `RibbonItem.Description` and `Shortcut` in Birko.Xaml.Core.
- **Every command and folded group** gets a `ToolTip` instance:
  - the name in semibold, ` (shortcut)` muted beside it, and the description under it in the secondary text colour;
  - the light look the user picked, from the surface tokens; the existing ToolTip theme binds them by
    `TemplateBinding`, so no theme change;
  - a 500ms hover delay; keyboard focus (Tab or arrows) opens it at once, a click does not;
  - the first Escape hides it, in the panel and in a folded group's popup.
- **Automation.** The description is the help text, the shortcut the accelerator key.
- **TASK-752 follow-up.** Escape in the panel (with no tooltip showing) goes back to the active tab, as b-ribbon's
  does. TASK-752 had not ported it.
- **Gallery demo.** Paste, Cut, Copy and Bold carry the web Playground's descriptions and shortcuts.
- **Test helper.** Old tests found commands by their string tip; they now read the heading through
  `RibbonTests.TipText`. The Small-variant test filters on-screen buttons, since every size has a tooltip now.
- **Gate.** New `RibbonTooltipTests`, 6 tests, all red on the code before. Birko.Xaml.Avalonia.Tests 229/229;
  Birko.Xaml.Core.Tests 60/60; the Gallery builds.

