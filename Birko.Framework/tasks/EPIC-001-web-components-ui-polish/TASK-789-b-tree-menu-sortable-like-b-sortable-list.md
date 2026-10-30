---
id: TASK-789
parent: EPIC-001
status: todo
priority: P2
assignee: ai
created: 2026-10-30
depends-on: []
blocks: []
pr: null
github-issue: null
jira-key: null
---

# b-tree-menu's sortable mode should reorder by drag and keyboard, in line with b-sortable-list

## Context

Asked by the user through the Symbio session on 2026-10-30. Symbio found it while moving its hand-made ▲▼ lists to
`b-sortable-list` ([[TASK-787]], Symbio TASK-1006): the Presentation page tree is the one list left whose arrows are the
framework's, so Symbio cannot fix it alone. Symbio's side is Symbio TASK-1012, blocked on this task.

**Today** (`Birko.Web.Components/src/nav/b-tree-menu.ts`, controls ~373–378, wiring ~467–487, drag ~491–558), with
`sortable` on, each node shows:
- a ▲ button, an editable order number (`.sort-order`) and a ▼ button (`.sort-btn`), all `tabindex="-1"`, so the
  keyboard cannot reach them. The titles "Move up" / "Move down" are hard-coded English;
- native HTML5 `draggable` on the row: drop zones before / inside / after (top 25%, middle, bottom 25%), emitting
  `reorder` `{ sourceId, targetId, position, sourceItem, targetItem }`. HTML5 drag does nothing on touch;
- the buttons emit `sort` `{ id, direction, item }`, the number input emits `sort-order` `{ id, sortOrder, item }`.

Nothing is announced, and there is no drop indicator in `b-sortable-list`'s style.

## Ask (from Symbio, to be decided with the user)

- A handle per node; drag by pointer (mouse, pen, touch hold) as in `b-sortable-list`, with its drop bar.
- Keyboard: Space/Enter picks a node up, arrows move it, Space drops, Escape puts it back; each step announced.
- Possibly moving a node into another parent (`reorder` already carries `position: 'inside'`).
- The same look as `b-sortable-list`.
- **Keep `reorder` compatible** — Symbio consumes it.

## Open decisions (preview first — a stakeholder-facing surface, as for TASK-787 / 788)

1. Do the ▲ / number / ▼ controls go, or stay behind an option? What happens to the `sort` and `sort-order` events
   (keep emitting, deprecate, remove — Symbio is the known consumer)?
   **Consumer usage (Symbio session, 2026-10-30):** four sortable trees, all `sortable: canUpdate`.
   `presentation/categories/categories-page.ts:185-197`, `presentation/navigation/navigation-page.ts:202-214` and
   `products/categories/categories-page.ts:183-195` listen to `sort`, `sort-order` **and** `reorder`;
   `presentation/pages/pages-page.ts:314` to `reorder` only (`building/structure-page.ts` is not sortable). So
   removing `sort` / `sort-order` breaks three Symbio pages; deprecating them (still emitted, e.g. alongside `reorder`)
   lets Symbio move all four trees to `reorder` in Symbio TASK-1012. Symbio's specs do not click `.sort-btn`.
2. Reparenting: allowed by keyboard too (e.g. →/← to indent / outdent) or only by drag? Any veto hook (a cancelable
   `reorder`, or a `canDrop(source, target, position)` callback) for trees where some nodes may not hold children?
3. Whole row drags or handle only (tree rows are also click-to-select and expand toggles — likely handle-only, or a
   hold)?
4. Collapsed parents: does hovering a collapsed parent during a drag expand it after a delay?
5. Share code with `b-sortable-list` (a common drag/keyboard helper) or reimplement within the tree?

## Steps

1. Preview options for decisions 1–4 for the user (Playground or a mockup); record the answers here.
2. Implement; keep `reorder`'s detail shape; localize every new label (`bwc.treeMenu.*`).
3. Playground smoke suite (keyboard moves, announcements, `reorder` detail, reparent, compat), plus a real-input
   drag check like `sortable-drag-check.mjs`; mutation-prove the key assertions.
4. API.md, ACCESSIBILITY.md, CHANGELOG, version bump; tell the Symbio session for TASK-1012.

## Human test plan

To be written once the decisions are taken: in the Playground's Tree menu card (sortable) — drag a node before / after
/ into another, by mouse and by keyboard, with a screen reader's announcements; then in Symbio, Presentation → Pages,
reorder and reparent a page (Symbio TASK-1012).
