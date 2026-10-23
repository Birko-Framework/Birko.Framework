---
id: TASK-755
parent: STORY-067
feature: FEATURE-019
status: verify  # verified headlessly 2026-10-23; the human test plan is left
priority: P3
assignee: ai
created: 2026-10-23
depends-on: [TASK-752]
blocks: []
pr: null
github-issue: null
jira-key: null
---

# Avalonia ribbon: split buttons and menu buttons

## Context

FEATURE-019 D13, split from TASK-742. Web counterpart: TASK-732. Desktop counterpart of the web behaviour, with the looks the user picked on the web (D13, decided 2026-10-23: same as web, no new previews). Shared model changes go in `Birko.Xaml.Core/Ribbon`, the control in `Birko.Xaml.Avalonia/Controls`. The narrow-window menu gets the same treatment as the web's mobile list.

`RibbonItem.Menu` and `Split`: a menu button opens a menu; a split button runs its command, and its ▾ part opens the menu (Office's "Paste ▾"); choices with icon, label, shortcut and separators; Alt+↓ opens; a choice reports the choice with its parent; inside a folded group the menu opens from the popup.

## Acceptance criteria

- [x] Menu and split buttons with the picked look (divider on hover); choices with icon, label, shortcut, separators, disabled
- [x] Enter / Space / Alt+↓ open at the first choice; arrows wrap; Escape returns to the button
- [x] A choice reports its id and its command; works inside a folded group and in the narrow menu; headless tests

## Out of scope

- Anything the web ribbon did not ship

## Human test plan

- [ ] Open Paste ▾ and CSV by mouse and keyboard, pick an option, check Paste still pastes; repeat inside a folded group

## Outcome (2026-10-23)

- **Model.** `RibbonItem.Menu` is a list of `RibbonMenuItem` choices (id, label, icon, shortcut, disabled, run) and
  `RibbonMenuEntry.Separator`, plus `Split`.
- **The menu** is a real `MenuFlyout`, so its arrows, Enter, Escape and automation roles are the toolkit's. A choice's
  shortcut is its `InputGesture` (shown right-aligned, display only) and its accelerator key.
- **A menu-only command** is a `RibbonMenuButton` with a ▾ caret. Its automation peer reports expand/collapse, the
  desktop form of `aria-haspopup` / `aria-expanded`.
- **A split command** has three parts:
  - the main command, which runs it;
  - a 1px divider, shown on hover or while either part holds focus (D3, option A);
  - a ▾ `RibbonMenuButton` named "More options: {label}", which opens the menu.
  - Large stacks the parts, with the name in the ▾ part.
- **Keyboard.** Alt+↓ opens the menu from either part. Both parts are arrow-key stops.
- **Events.** `MenuItemInvoked` (tab, choice, parent) fires after the choice's `Run`.
- **Folded groups and the narrow menu.** A menu inside a folded group's popup opens over it and the popup stays open.
  The narrow menu lists the choices under their command; a menu-only command is their heading there.
- **Gallery demo.** Paste is a split (as text, as picture, special…) and CSV a menu, as in the web Playground.
- **Gate.** New `RibbonMenuTests`, 9 tests, which do not compile against the code before:
  - the menu button; the split (main runs, ▾ opens); choices (icon, shortcut, separator, disabled);
  - a choice runs and reports with its parent; Alt+↓ and the expand/collapse state; both parts as arrow stops;
  - the Large stack; a menu from a folded group's popup; the narrow menu; screenshots in Medium and Large.
  - Birko.Xaml.Avalonia.Tests 245/245; Birko.Xaml.Core.Tests 60/60; the Gallery builds.

