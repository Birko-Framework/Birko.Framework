---
id: TASK-759
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

# Avalonia ribbon: Quick Access Toolbar (the app saves it)

## Context

FEATURE-019 D13, split from TASK-742. Web counterpart: TASK-737. Desktop counterpart of the web behaviour, with the looks the user picked on the web (D13, decided 2026-10-23: same as web, no new previews). Shared model changes go in `Birko.Xaml.Core/Ribbon`, the control in `Birko.Xaml.Avalonia/Controls`. The narrow-window menu gets the same treatment as the web's mobile list.

Opt-in favourite commands before the tabs (or above / below): add and remove from a right-click, customise and reset from its ▾. Decided 2026-10-23: the app saves the choice; the ribbon exposes it and raises a change event, with no file I/O in the control.

## Acceptance criteria

- [x] Opt-in toolbar with the app's defaults; right-click / Shift+F10 add and remove; ▾ customises and resets
- [x] The choice is exposed (get / set) with a change event; the control writes no file
- [x] One Tab stop with arrows; toggles kept in step; headless tests

## Out of scope

- Anything the web ribbon did not ship

## Human test plan

- [x] Add and remove commands, reset, restart the Gallery: the app restores the toolbar

## Outcome (2026-10-23)

- **Opt-in.** An app turns the toolbar on with its default commands; without that there is no toolbar and a
  right-click offers nothing. It sits before the tabs by default, or on its own row above the tabs or under the
  ribbon.
- **Adding and removing.** A right-click (or Shift+F10, or the menu key) on a ribbon command offers "Add to Quick
  Access Toolbar", or "Remove" when it is already there; on a toolbar button, "Remove". A menu-only button cannot be
  added; a split button can.
- **The ▾.** Lists each default with a check (unchecking hides it; checking it again puts it back in its own place),
  the commands the user added, and Reset.
- **The app saves it (the user's decision).** The ribbon hands the toolbar over as a list of references and raises a
  change event on every edit; the app saves the list and gives it back at start. The control writes no file.
  References that no longer resolve (a command removed from the ribbon) are dropped. The Gallery saves it under the
  user's local app data, so the human test plan's restart works.
- **Keyboard and key tips.** One Tab stop, arrows, Home and End inside. Alt shows 1–9 on the toolbar beside the tabs'
  letters, as Office and the web do.
- **Toggles.** A toggle added to the toolbar is the same toggle: pressing either copy updates both.
- **Gate.** New `RibbonQuickAccessTests`, 12 tests with real headless right-clicks and keys:
  - off by default; defaults before the tabs and running on press;
  - right-click adds, again removes, and the change event carries the references; right-click on the toolbar removes;
  - the ▾ hides, restores in place and resets; saved references restore and drop what no longer resolves;
  - above and below; one Tab stop with arrows; digits as key tips; a toggle in step; a screenshot.
  - Birko.Xaml.Avalonia.Tests 277/277; Birko.Xaml.Core.Tests 60/60; the Gallery builds.

