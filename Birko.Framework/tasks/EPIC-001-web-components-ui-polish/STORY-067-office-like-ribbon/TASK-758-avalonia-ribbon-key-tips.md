---
id: TASK-758
parent: STORY-067
feature: FEATURE-019
status: todo
priority: P3
assignee: ai
created: 2026-10-23
depends-on: [TASK-752, TASK-755]
blocks: []
pr: null
github-issue: null
jira-key: null
---

# Avalonia ribbon: key tips (plain Alt)

## Context

FEATURE-019 D13, split from TASK-742. Web counterpart: TASK-736. Desktop counterpart of the web behaviour, with the looks the user picked on the web (D13, decided 2026-10-23: same as web, no new previews). Shared model changes go in `Birko.Xaml.Core/Ribbon`, the control in `Birko.Xaml.Avalonia/Controls`. The narrow-window menu gets the same treatment as the web's mobile list.

Tapping and releasing plain Alt shows letters on the tabs (decided 2026-10-23: plain Alt, as in desktop Office; rebindable). The levels follow the web: tabs, then a tab's commands, then a folded group, menu or gallery; Escape steps back; letters are automatic with `Keytip` overrides; dark squares as picked on the web.

## Acceptance criteria

- [ ] Plain Alt (tap and release) shows tab letters; a letter opens the tab and shows command letters
- [ ] Levels for folded groups, menus and galleries; Escape steps back; a click or focus loss ends it; Alt inside an input does nothing
- [ ] Automatic letters with `Keytip` overrides; dark squares; rebindable; headless tests

## Out of scope

- Anything the web ribbon did not ship

## Human test plan

- [ ] Reach three different commands with key tips only, one in a menu and one in a folded group
