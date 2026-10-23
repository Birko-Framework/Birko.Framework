---
id: TASK-756
parent: STORY-067
feature: FEATURE-019
status: verify  # verified headlessly 2026-10-23; the human test plan is left
priority: P3
assignee: ai
created: 2026-10-23
depends-on: [TASK-751]
blocks: []
pr: null
github-issue: null
jira-key: null
---

# Avalonia ribbon: group dialog launcher

## Context

FEATURE-019 D13, split from TASK-742. Web counterpart: TASK-733. Desktop counterpart of the web behaviour, with the looks the user picked on the web (D13, decided 2026-10-23: same as web, no new previews). Shared model changes go in `Birko.Xaml.Core/Ribbon`, the control in `Birko.Xaml.Avalonia/Controls`. The narrow-window menu gets the same treatment as the web's mobile list.

`RibbonGroup.Launcher` / `LauncherLabel`: Office's small corner arrow at the group's bottom right, beside its name; a click, Enter or its key tip raises a launcher event; named and tooltipped; in a folded group it moves into the popup's header.

## Acceptance criteria

- [x] Corner arrow beside the group name, the name staying centred
- [x] Click / Enter raise the launcher event, not a command click
- [x] Named and tooltipped; in the popup header when folded; headless tests

## Out of scope

- Anything the web ribbon did not ship

## Human test plan

- [ ] Click and keyboard-activate the Font launcher in the Gallery, wide and folded

## Outcome (2026-10-23)

- **Model.** `RibbonGroup.Launcher` and `LauncherLabel`, plus an optional `RibbonGroup.Id`: desktop groups had none,
  and the event reports `Id ?? Label`.
- **Look (D4, Office's corner arrow).** A small button at the group's bottom right, sized by the launcher tokens, with
  a vector arrow. It sits over the name, which is padded by the launcher's width on both sides, so it stays centred and
  its descenders keep their room.
- **Behaviour.**
  - Named and tooltipped "More {group} options", or its `LauncherLabel`.
  - An arrow-key stop: ↓ from the command above reaches it.
  - A press raises `GroupLauncherInvoked` and runs no command.
  - Inside a folded group's popup, a press closes the popup.
  - The narrow menu lists it under its group.
- **Not like the web.** In a folded popup the launcher stays beside the group's name at the bottom of the popup
  content. b-ribbon moves it into a header at the top.
- **Gallery demo.** The Font group has a launcher, and the demo line reports "Font dialog".
- **Gate.** New `RibbonLauncherTests`, 6 tests, which do not compile against the code before:
  - name, label and tooltip; reports only itself;
  - bottom right with the name centred, clear of it and not clipped; a keyboard stop;
  - in the folded popup, which it closes; the narrow menu; a screenshot.
  - Birko.Xaml.Avalonia.Tests 251/251; Birko.Xaml.Core.Tests 60/60; the Gallery builds.

