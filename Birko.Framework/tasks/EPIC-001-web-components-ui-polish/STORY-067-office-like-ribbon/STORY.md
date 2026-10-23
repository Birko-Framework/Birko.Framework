---
id: STORY-067
parent: EPIC-001
feature: FEATURE-019
status: done  # FEATURE-019 signed off by the user 2026-10-23
created: 2026-10-22
---

# An Office-like `b-ribbon`

## User story

As a user of a Birko web app, I want the ribbon to look and behave like the Office ribbon I know — even groups,
aligned commands, arrow keys, menus, key tips, tooltips — so I can use it without learning a second ribbon.

## Behaviour

The decisions are FEATURE-019's ([decisions.md](../../../docs/features/FEATURE-019-office-like-ribbon/decisions.md)),
one task each. Web first; the Avalonia ribbon's parity is TASK-742, after the web tasks.

Order: TASK-730 (layout) first, since later visuals (the launcher beside the bottom label) build on it.
TASK-731 (arrow keys) unblocks TASK-736 (key tips) and TASK-741 (inputs); TASK-732 (menus) unblocks TASK-740
(galleries) and, with TASK-731, TASK-736.

## Tasks

- TASK-730 — b-ribbon: equal-height groups, aligned rows, group labels at the bottom
- TASK-731 — b-ribbon: arrow-key navigation between commands and groups
- TASK-732 — b-ribbon: split buttons and dropdown menu buttons
- TASK-733 — b-ribbon: group dialog launcher
- TASK-734 — b-ribbon: contextual tabs
- TASK-735 — b-ribbon: collapse by double-clicking a tab, and Ctrl+F1
- TASK-736 — b-ribbon: key tips (Alt shows letters for keyboard access)
- TASK-737 — b-ribbon: Quick Access Toolbar
- TASK-738 — b-ribbon: rich tooltips (title, description, shortcut)
- TASK-739 — b-ribbon: toggle buttons with a pressed state
- TASK-740 — b-ribbon: galleries
- TASK-741 — b-ribbon: inputs and comboboxes inside the ribbon
- TASK-742 — Avalonia Ribbon: parity with the Office-like b-ribbon (FEATURE-019)
- TASK-743 — b-ribbon: a folded group without an icon is an empty 7px button (found by TASK-730)
