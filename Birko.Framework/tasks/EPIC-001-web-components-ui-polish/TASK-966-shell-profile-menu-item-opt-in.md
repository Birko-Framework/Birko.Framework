---
id: TASK-966
parent: EPIC-001
feature: null
status: todo
priority: P3
assignee: ai
created: 2026-11-22
depends-on: []
blocks: []
related: [TASK-965]
findings: []
pr: null
github-issue: null
jira-key: null
---

# The shell's default user menu offers Profile in apps that have no profile page

## Context

Reported by DraCode (its TASK-056), 2026-11-22. `BCoreAppShell.getUserMenuItems()` always includes a Profile item that
goes to `getRoutes().profile` (`#/profile`), even when the app has no such route; DraCode overrides the menu to
Settings and Sign out. DraCode suggests making Profile opt-in.

Undecided (the user filed it to decide later, 2026-11-22). Making it opt-in changes the default menu: an app that
relies on the default Profile item (check Symbio first) would lose it unless it opts in.

## Acceptance criteria

- [ ] Decided with the user: opt-in, or kept as is with the override documented.
- [ ] If opt-in: the default menu has no Profile item unless the app asks for it; the consumers that use it opt in;
      CHANGELOG marks it as a behaviour change.

## Human test plan

To be written once decided.
