---
id: TASK-934
parent: EPIC-001
feature: null
status: done
priority: P2
assignee: ai
created: 2026-11-15
depends-on: []
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# `b-dropdown-menu`: a non-clickable group heading

Requested by the Symbio session for Symbio TASK-1071 (user menu layout): the language rows of the user menu need a
"Language" label above them that is not itself a menu item.

## Change (Birko.Web v0.4.15)

- `DropdownItem.heading?: string`, mirrored on Birko.Web.Shell's `MenuItem`. It renders a small muted label before the
  item, after its divider if `divider` is set.
- The heading opens a `role="group"` named by the label (`aria-labelledby`). The group holds that item and the ones
  after it, up to the next divider or heading. The label itself is `aria-hidden` (it is announced as the group's name),
  is not focusable, and arrow-key navigation skips it. The items stay ordinary `menuitem`s.
- Items without `heading` render exactly as before.
- Playground: the gallery's dropdown demo shows a heading. It previously used a blank `label: ''` item to carry the
  divider, which rendered as an empty menu item; that is gone.

## Acceptance criteria

- [x] The heading renders after the divider and before its item, inside a `role="group"` named by it.
- [x] It is not focusable; ArrowDown/ArrowUp skip it; clicking it selects nothing and keeps the menu open.
- [x] Items without a heading render no group or heading.
- [x] The user confirmed the look (screenshot of the Playground gallery menu, 2026-11-15).

## Evidence

Playground backport-smoke: 3 of the 8 TASK-934 checks fail against Birko.Web main (no heading rendered), and it
passes 356/356 with the change. verify.mjs reports 0 failing checks. keyboard-check passes 225/225, and
a11y-name-check and a11y-description-check pass. `tsc --noEmit` is clean for Components and Shell.

## Human test plan

1. Run the Playground, open Gallery → Layout, and open "Actions ▾" on the Dropdown menu card.
2. A divider appears under Duplicate, then a small muted "Language" label, then English and Slovenčina.
3. Clicking "Language" does nothing and the menu stays open. Arrow keys move Edit → Duplicate → English → Slovenčina →
   Delete and never stop on the label.
4. With NVDA, focusing English announces the "Language" group.
