---
id: TASK-570
parent: EPIC-015
feature: FEATURE-015
status: done
priority: P2
assignee: ai
created: 2026-10-12
depends-on: []
blocks: []
pr: null
github-issue: null
jira-key: null
---

# The restyled TreeViewItem cannot be expanded or collapsed by the user

## Context

Reported from FinStat's desktop API tester ("the tree view does not work properly"), whose response viewer
is `ObjectTree` over the restyled `TreeView`. Compared against Avalonia's own Fluent `TreeViewItem` template,
`Controls/Tree.axaml` had three defects:

- **The chevron was not bound to `IsExpanded`.** Fluent binds `IsChecked="{TemplateBinding IsExpanded,
  Mode=TwoWay}"`. Unbound, clicking the chevron only rotated the arrow — the node never opened or closed —
  and a node created expanded drew the collapsed `›`.
- **Header double-tap did nothing.** `TreeViewItem` wires double-tap only to a template part named
  `PART_Header`; the header border was named `header`.
- **Hover lit every ancestor.** The highlight used `^:pointerover`, and an item stays pointer-over while the
  mouse is on any descendant row, so hovering a nested node highlighted the whole path above it.

The existing `TreeTests` only set `IsExpanded` from code, which is why none of this was caught.

## Acceptance criteria

- [x] The chevron reflects the initial `IsExpanded` and toggling it expands/collapses the item
- [x] The header part is `PART_Header`, so double-tapping a header toggles the node
- [x] Hover highlights the hovered header only, not its ancestors
- [x] Regression tests fail on the old template and pass on the new one

## Human test plan

Desktop tester (or any `ObjectTree` host): load a response, click a chevron (opens/closes), double-click a
row header (opens/closes), hover a nested row (only that row highlights).

## Review log

- 2026-10-12 — Three tests added to `TreeTests` (`Chevron_reflects_the_initial_expanded_state`,
  `Toggling_the_chevron_expands_and_collapses_the_item`, `Header_part_is_named_for_double_tap_expansion`);
  all fail against the previous `Tree.axaml` and pass now. Full `Birko.Xaml.Avalonia.Tests` green.
