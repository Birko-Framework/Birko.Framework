---
id: TASK-787
parent: EPIC-001
status: todo
priority: P2
assignee: ai
created: 2026-10-29
depends-on: []
blocks: []
pr: null
github-issue: null
jira-key: null
---

# A reorderable list component — drag and drop, keyboard, and a searchable "pick and order" variant

## Context

Asked by the user on 2026-10-29 while walking Symbio TASK-825 (a ProductGrid block's manual product list). Their words:
"maybe we need a birko.web component for it that it is drag and dropable and needs some visual love".

**Measured:** Birko.Web.Components has no reusable reorderable list. Drag and drop exists only inside specific components
(`b-kanban`, `b-table` rows, `b-gallery`, `b-tabs`, `b-tree-menu`), and none of them is a plain ordered list a form can
use.

So consumers hand-roll ▲▼ buttons. Symbio does it three times in its page editor:
- the page's block list;
- a ProductGrid block's manual product list (search, pick, reorder; TASK-825);
- a Gallery block's image rows (TASK-900).

`b-multi-select` was considered for the pick-and-order case and rejected in Symbio TASK-825. It renders its chips from
its option list, so it loses the author's order and hides a pick during a search.

## Acceptance criteria

- [ ] **`b-sortable-list`**, a generic ordered list:
  - each row's content is slotted or rendered by the consumer;
  - reorder by **drag and drop**, with a visible drag handle and a drop indicator;
  - reorder by **keyboard**, for example the handle focused plus arrow keys, or Alt+↑/↓;
  - an optional per-row Remove;
  - it emits one `change` event with the new order (ids), and the consumer owns the data;
  - pointer events, so touch works.
- [ ] **Layouts** (asked by the user, 2026-10-29), one attribute, e.g. `layout="list" | "grid"`:
  - `list`: one item per line, full width. The default, for text rows such as blocks and a product list.
  - `grid`: items wrap into tiles, for image-like rows such as Gallery images and product cards. Tile size comes from a
    token or attribute (e.g. `item-min-width`), and the grid reflows to the container's width.
  - Drag and drop works in **two dimensions** in `grid`: the drop indicator shows the target slot between tiles, not
    just above or below.
  - Keyboard moves follow reading order in both layouts: ←/→ move one place in `grid`, and ↑/↓ move one row of tiles
    there.
  - Consider a third, `inline` (chips that wrap, for short labels such as tags). Add it if it costs only CSS; otherwise
    record why not.
  - The same `change` event and order semantics in every layout. The layout is presentation only.
- [ ] **Accessibility.** The handle has an accessible name naming the row. A move is announced through a live region,
      e.g. "Moved Shaggy fur to position 1 of 3". Focus stays on the moved row.
- [ ] **`b-ordered-picker`** (name to be confirmed), search plus a `b-sortable-list` of what is chosen:
  - the consumer supplies the search: a function returning `{ id, label, meta? }` for a query, server-side as in
    Symbio's `wireSearchableSelect`;
  - picking appends at the end; a duplicate pick gives feedback rather than doing nothing silently;
  - a chosen item's label survives a search that does not return it;
  - optional `max`, with a notice when the list goes past it;
  - its value is the ordered id array, as a form-associated element if STORY-023's pattern allows.
- [ ] **Visual polish.** Rows follow the design tokens (`--b-*`): a hover state, a lifted dragging row, a drop gap, an
      empty state, a compact size, and a dark theme.
- [ ] Playground pages for both, plus smokes. Assert the order after a drag, after keyboard moves and after a search,
      **in each layout**; in `grid`, include a drag across rows. Mutation-prove the order assertion.
- [ ] CHANGELOG entry and package version bump. Docs: the component reference.

## Out of scope

- Replacing the drag and drop inside `b-kanban`, `b-table`, `b-gallery`, `b-tabs` or `b-tree-menu`.
- Adopting the component in Symbio. Symbio files that on its side (Symbio TASK-1006).

## Human test plan

1. Playground: drag a row to a new position, then move one with the keyboard. The order and the announcement are right.
2. Playground ordered picker: search, pick three, reorder, then search for something else. The chosen list keeps its
   labels and order.
3. On a touch device or touch emulation, drag still works.
4. Switch the Playground list to `grid`: tiles wrap with the container's width, and a tile can be dragged into the
   previous row.
