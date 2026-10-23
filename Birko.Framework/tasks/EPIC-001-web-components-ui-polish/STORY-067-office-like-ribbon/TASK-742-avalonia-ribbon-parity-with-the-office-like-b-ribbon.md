---
id: TASK-742
parent: STORY-067
feature: FEATURE-019
status: done  # split into TASK-751–763 (2026-10-23)
priority: P3
assignee: ai
created: 2026-10-22
depends-on: [TASK-730, TASK-731, TASK-732, TASK-733, TASK-734, TASK-735, TASK-736, TASK-737, TASK-738, TASK-739, TASK-740, TASK-741]
blocks: []
pr: null
github-issue: null
jira-key: null
---

# Avalonia Ribbon: parity with the Office-like b-ribbon (FEATURE-019)

## Context

FEATURE-019 D13 (approved by the user 2026-10-22): The desktop ribbon (Birko.Xaml.Avalonia) gets the same behaviours as the web one, after the web version.

Birko.Xaml.Avalonia has its own Ribbon (Ribbon.cs, RibbonGroupsPanel.cs, RibbonChunkButton.cs), which mirrors the web ribbon's scaling. The user chose web first, Avalonia later.

## Acceptance criteria

- [x] Split once the web tasks settle: one Avalonia task per web behaviour that still applies
- [x] Same layout rules (equal heights, aligned rows, labels below) and the same keyboard model (carried by TASK-751 and TASK-752)

## Out of scope

- Anything the web ribbon did not ship

## Human test plan

- [x] Written per split task (TASK-751–763)

## Split (2026-10-23)

The user chose the plan: all 13, built in this order. Decided with it: key tips start with **plain Alt** on the desktop
(as in desktop Office; the web uses Alt+Windows because the browser owns Alt); the **app saves** the Quick Access
Toolbar (the control exposes it and raises a change event); the **web looks are reused**, with no new previews.

| Task | Desktop behaviour | Web |
|---|---|---|
| TASK-751 | Layout: equal heights, aligned rows, labels below, dividers halfway, centred narrow groups, initials | 730, 743, 746 |
| TASK-752 | Keyboard: one Tab stop, arrows by position | 731 |
| TASK-753 | Rich tooltips | 738 |
| TASK-754 | Toggle buttons and one-of sets | 739 |
| TASK-755 | Split and menu buttons | 732 |
| TASK-756 | Dialog launcher | 733 |
| TASK-757 | Collapse by double-clicking a tab (Ctrl+F1 exists) | 735 |
| TASK-758 | Key tips (plain Alt) | 736 |
| TASK-759 | Quick Access Toolbar (the app saves it) | 737 |
| TASK-760 | Contextual tabs | 734 |
| TASK-761 | Galleries | 740 |
| TASK-762 | Input boxes and dropdowns | 741 |
| TASK-763 | Every size a theme resource | 747 |

Not carried over: link commands in the toolbar (TASK-748, the desktop has no links), the tooltip strip (TASK-749, a web
layout defect) and the carousel (TASK-745).

