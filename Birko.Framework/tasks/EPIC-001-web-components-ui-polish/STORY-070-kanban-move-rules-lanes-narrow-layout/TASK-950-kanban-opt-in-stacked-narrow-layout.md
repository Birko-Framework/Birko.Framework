---
id: TASK-950
parent: STORY-070
feature: FEATURE-022
status: done
priority: P2
assignee: ai
created: 2026-11-21
depends-on: [TASK-948]
blocks: []
pr: null
github-issue: null
jira-key: null
---

# b-kanban: an opt-in stacked layout for a narrow board

## Context

FEATURE-022 D6 (approved by the user 2026-11-21): a board stacks its columns one under another, with no sideways
scrolling, only when the page asks for it and only when the board itself is narrower than the width the page gives;
lanes stay grouped under their headings when stacked.

Measured on the board's own width (a container query), so a board beside a docked chat stacks too. Without the opt-in
nothing changes, so Symbio's three boards keep scrolling sideways.

Decisions: [FEATURE-022](../../../docs/features/FEATURE-022-kanban-move-rules-lanes-narrow-layout/decisions.md).

## Acceptance criteria

- [x] A page can opt in with a width; below it the board stacks one column per row with no horizontal scroll.
- [x] It follows the board's own width, not the window's.
- [x] Lanes stay grouped under their headings when stacked; a rows board falls back to sections when stacked and returns
      to rows when wide (D19); tags stack as plain lists.
- [x] Without the opt-in the board looks and scrolls exactly as before.
- [x] Drag and keyboard moves still work when stacked.
- [x] Checked at phone width and beside a narrow container (device-fix-check or an equivalent), in light and dark.

## Evidence

Birko.Web `task/TASK-947` 81edc45 (v0.4.21), Playground 1600204:
- backport-smoke: 7 TASK-950 checks. Before the change 3 failed (a narrow board stacking; rows falling back to sections;
  tags stacking); the other 4 held already (un-stacking and returning to rows, trivially, since nothing stacked; the
  no-`stackBelow` baseline; a drag). After: 459/459, 0 failing checks. A first pass left 2px of sideways overflow
  (column borders on top of `width: 100%`), fixed with `box-sizing: border-box`.
- device-fix-check 247/247: at a 390px phone viewport, in light and dark, a `stackBelow` board stacks its 3 columns and
  nothing is wider than the screen (written after the code; its red proof is the backport-smoke checks above).
- keyboard 237/237; verify 0 failing; a11y-name OK; rerender-leak PASS (the ResizeObserver adds no listeners per
  render); entry-points 433/433; locale-keys 23/23; contextual-ribbon 13/13; Components `tsc` clean. All from builds
  that printed "✓ Built". The gallery kanban opts in at 44rem.

**Signed off** by the user (František Bereň, maintainer) on 2026-11-22, the human test plan below run in the Playground
from the `task/TASK-947` build: "All passed".

## Human test plan

1. On a board that opts in, narrow its container below the width: columns stack, with no sideways scroll, and the
   To-do lanes stay grouped under their headings.
2. Widen it again: the columns sit side by side.
3. Open one of Symbio's boards on a phone: it still scrolls sideways (it has not opted in).

## Implementation plan

Drafted 2026-11-21 on the integration branch `task/TASK-947`. Decided (the user): D19 (rows fall back to sections when
stacked), D20 (`KanbanConfig.stackBelow`).

1. `KanbanConfig.stackBelow?: string` (a CSS length). A container query cannot take its width from config, so a
   `ResizeObserver` on the board measures its own width against the length (resolved through a hidden probe, so rem,
   em and px all work) and sets a `stacked` attribute on the host; it re-renders only when that changes.
2. `:host([stacked])`: columns one per row, full width, no horizontal scroll; lane sections grouped as they are.
3. The effective lane mode is sections while a rows board is stacked (D19); tags unchanged.
4. Without `stackBelow`, nothing changes: the board scrolls sideways as before.
5. Docs, CHANGELOG; the gallery kanban opts in at 44rem.

**Tests** (red first): a narrow container stacks (the `stacked` attribute, columns one per row at full width, no
horizontal overflow); a wide one does not; no `stackBelow` never stacks; rows fall back to sections when narrow and
come back when widened; tags stack with their tags; a drag still works stacked. And device-fix-check at a phone
viewport in light and dark: no horizontal overflow.
