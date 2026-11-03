---
id: TASK-808
parent: EPIC-001
feature: null
status: in-progress
priority: P1
assignee: ai
created: 2026-11-03
depends-on: []
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# b-ribbon: an active split button is tinted as a whole tile

Reported by the user on Symbio (screenshot 2026-11-03, Obchod tab, E-Shop active on `#/eshop/payments`).

## Context

Since v0.4.1 (TASK-803) every module with two or more pages in the Shell's category ribbon is a split button. The
active module's tint (`.ribbon-item.active`) sits on the split's main part only. In a large group (Symbio's shell)
the parts stack: the main part is the icon, the ▾ part below carries "E-Shop ▾". So the active module lit only its
icon, while an active plain command (Platby) is tinted whole. Under the pointer it was worse: the split's hover rule
(`.ribbon-split:hover > …`, more specific) replaced the tint with the hover grey, where a plain active command keeps
its tint.

## Acceptance criteria

- [x] An active split button is tinted on both parts, its caret in the tint's text colour, at large and medium size.
- [x] It keeps the tint under the pointer, as an active plain command does.
- [x] An inactive split button is unchanged.
- [x] Checks that fail without the fix: category-ribbon-smoke (whole tint, caret, inactive untouched) and
      category-ribbon-check (stacked at large size, tinted whole; the same tint under the real pointer).
- [ ] Human test passed, Symbio told before it reaches Birko.Web main.

## Out of scope

- TASK-806 (the tooltip over an open menu).

## Human test plan

- [ ] In the Playground's "Category ribbon (Shell)" card (and in Symbio, Obchod → E-Shop → any page): the active
      module's tile is tinted from the icon down through "Label ▾", like an active page button, and stays so under the
      mouse. Other modules are untinted.

## Implementation plan

1. Render `active` on the `.ribbon-split` wrapper when the item is active.
2. CSS after the large/flyout split rules: `.ribbon-split.active > .ribbon-split-main, .ribbon-split.active >
   .ribbon-split-arrow` take the active tint (equal specificity to the hover rule, declared later, so it wins);
   `.ribbon-split.active .ribbon-caret` inherits the colour.
3. Checks in the Playground (smoke + real-input), mutations; v0.4.2.

## Built (2026-11-03), on branches, not merged

Birko.Web `task/TASK-808` 6540e43, v0.4.2 (worktree `Web.TASK-808`); Playground `task/TASK-808` 14042dc (worktree
`Birko.Web.Playground.TASK-808`). Smoke 28/28 and category-ribbon-check 16/16 with the fix; on Birko.Web main the new
checks fail (14/16, 27/28). Mutations caught: main part left to `.ribbon-item.active` (hover grey on the icon), caret
rule removed. verify.mjs 0 failing, keyboard-check 222/222, ribbon-a11y-check 22/22, entry-points-check 427/427,
device-fix and rerender-leak checks pass.
