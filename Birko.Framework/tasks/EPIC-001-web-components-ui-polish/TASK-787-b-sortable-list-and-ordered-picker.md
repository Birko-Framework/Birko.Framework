---
id: TASK-787
parent: EPIC-001
status: verify
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

- [x] **`b-sortable-list`**, a generic ordered list:
  - each row's content is slotted or rendered by the consumer;
  - reorder by **drag and drop**, with a visible drag handle and a drop indicator;
  - reorder by **keyboard**, for example the handle focused plus arrow keys, or Alt+↑/↓;
  - an optional per-row Remove;
  - it emits one `change` event with the new order (ids), and the consumer owns the data;
  - pointer events, so touch works.
- [x] **Layouts** (asked by the user, 2026-10-29), one attribute, e.g. `layout="list" | "grid"`:
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
- [x] **Accessibility.** The handle has an accessible name naming the row. A move is announced through a live region,
      e.g. "Moved Shaggy fur to position 1 of 3". Focus stays on the moved row.
- [x] **`b-ordered-picker`** (name to be confirmed), search plus a `b-sortable-list` of what is chosen:
  - the consumer supplies the search: a function returning `{ id, label, meta? }` for a query, server-side as in
    Symbio's `wireSearchableSelect`;
  - picking appends at the end; a duplicate pick gives feedback rather than doing nothing silently;
  - a chosen item's label survives a search that does not return it;
  - optional `max`, with a notice when the list goes past it;
  - its value is the ordered id array, as a form-associated element if STORY-023's pattern allows.
- [x] **Visual polish.** Rows follow the design tokens (`--b-*`): a hover state, a lifted dragging row, a drop gap, an
      empty state, a compact size, and a dark theme.
- [x] Playground pages for both, plus smokes. Assert the order after a drag, after keyboard moves and after a search,
      **in each layout**; in `grid`, include a drag across rows. Mutation-prove the order assertion.
- [x] CHANGELOG entry and package version bump. Docs: the component reference.

## Decisions (2026-10-29, the user, on a clickable preview)

Preview: https://claude.ai/artifact/THgTdfDKxaNjfZn8qHMhEK (version 2 carries all of these).

1. **Keyboard: pick up, move, drop.** Space or Enter on the handle picks the row up; arrows move it (↑/↓ by a row of
   tiles in `grid`); Space drops, Escape puts it back, Tab away drops it where it is. One `change` event, on the drop.
   Chosen over "arrows move at once": screen readers keep the arrow keys in browse mode, and a stray arrow moved a row
   with no undo.
2. **The whole row drags; `handle-only` when rows hold controls.** By default a press anywhere on a row drags it,
   except on its own buttons, links and fields. With `handle-only` only the handle does.
3. **`max` is hard.** At the limit a pick is refused with a notice ("The grid shows 5. Remove one to add another.").
   Only a list that is already over it (the limit lowered after picking) shows the extra rows below a line, with a
   notice; nothing is dropped silently. **A long list scrolls** inside a max height, auto-scrolling while a row is
   dragged near its top or bottom edge, and a keyboard move scrolls the row into view.
4. **The picker keeps its Add button**, the same height as its search input.
5. **`inline` ships** (CSS only).
6. **Name: `b-sortable-picker`**, not `b-ordered-picker`.
7. **Touch (proposed with 2, built, not yet confirmed by the user):** a row drags after a 0.35 s press and hold, so a
   swipe still scrolls the page; the handle drags at once. Human test step 3 is where it gets confirmed or changed.

8. **Where the picker's messages show is the consumer's** (asked by the user after the build, approved 2026-10-29):
   each fires a cancelable `notice` `{ kind, text, item? }`; untaken, `notify="toast"` sends `duplicate` / `max` to the toast,
   else they show under the search. `over` is a state: it stays inline under `notify="toast"`, and fires
   `over-cleared` when it ends. sortable-smoke 60/60; mutation-proven (a taken notice still shown: 58/60;
   `notify` ignored: 58/60).

## Resolution (2026-10-29)

Birko.Web `700dd50` (joins v0.1.22 with TASK-786; not pushed or tagged), Playground `8b5e578`.

- **`b-sortable-list`** (`data/`): the rows are the element's light-DOM children with `data-id` (`data-label` names
  one, else its text). Each child gets a generated `slot` name and a shadow `<li>` (handle, slot, Remove); the list
  moves the children themselves, so their order is always the order. A press on a row's own control never drags.
  `layout` list / grid / inline, `removable`, `numbered`, `handle-only`, `limit`, `max-height` (auto-scroll while
  dragging: its own scroller, else the nearest scrolling ancestor, else the page), `item-min-width`, `size="sm"`.
  `change` `{ order, id, from, to, removed? }`, `remove` cancelable, slot `empty`, `getOrder()`, `setItems()`,
  `highlight()`. `limit` and `layout` re-sync the rows and never re-render (a re-render would drop the focus).
- **`b-sortable-item`** (`data/`): a ready-made row (image, label, meta); a placeholder initial in `grid` when there is
  no image, so tiles keep one shape.
- **`b-sortable-picker`** (`inputs/`, form-associated, one `FormData` entry per id): `setSearch()`, debounced 200 ms,
  stale answers dropped; while a new search is pending, Enter and Add pick nothing from the previous results (found by
  the smoke). The picker stops the list's own `change` / `remove` and emits its own `change` `{ name, value, items }`
  and `reject` `{ item, reason }`. Results in a top-layer popover like `b-select`, so a modal does not clip them.
- **Inline ships** (decision 5): it was CSS only.

**Gate:** Playground `sortable-smoke` 53/53 in Chromium and Firefox. `sortable-drag-check.mjs` 13/13 with a real mouse
and touchscreen: a drag per layout, a grid tile into the row above with the drop bar measured in the gap,
`handle-only`, Escape mid-drag, auto-scroll, hold vs swipe. **Mutation-proven:**

| Planted defect | sortable-smoke | sortable-drag-check |
|---|---|---|
| drop slot off by one | 52/52 | **8/13** |
| grid ↓ moves one place | **48/52** | 13/13 |
| `change` carries the old order | **45/52** | **8/13** |

verify.mjs 0 failing three runs in a row (`sortable-smoke` waits for the modal-opening suites: 39/50 inside verify.mjs
before, 52/52 alone), entry-points 427/427, locale-keys 22/22, keyboard-check 222/222, device-fix 140/140, a11y-name 43.
Screenshots of every layout, the limit line, the picker and the empty state, in light and dark, looked over.

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
