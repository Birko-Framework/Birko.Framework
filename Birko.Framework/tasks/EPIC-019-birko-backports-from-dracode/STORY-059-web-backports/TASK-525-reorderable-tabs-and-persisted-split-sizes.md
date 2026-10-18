---
id: TASK-525
parent: STORY-059
feature: null
status: done
priority: P3
assignee: ai
created: 2026-10-07
depends-on: []
blocks: []
pr: null
github-issue: null
jira-key: null
---

# b-tabs drag-and-drop reorder; b-split-panel size persistence; a layout serialize/restore hook

## Context

DraCode TASK-058 needs drag-and-drop tab reordering and saved workspace layouts. `b-tabs` and `b-split-panel` have no
drag or persistence support today (only `b-kanban` drags), so the plan was to build them app-side.

Consumer side: DraCode `EPIC-018 / STORY-039` (`Consumers/DraCode/tasks/EPIC-018-adopt-birko-upstreams`).

## Acceptance criteria

- [x] `b-tabs` can be made reorderable by drag (and by keyboard), emitting the new order
- [x] `b-split-panel` exposes and accepts its sizes, and can persist them under a key
- [x] A serialize/restore hook lets an app save a layout (tab order + split sizes) wherever it likes
- [x] Component tests; accessible keyboard path

## Out of scope

- Server-side layout storage (apps)

## Human test plan

- [x] Drag tabs and resize a split in the Birko.Web playground; reload restores the layout

## Outcome (2026-10-18)

Released in **Birko.Web v0.1.12** (Web `fed4a66`, tag `v0.1.12`), with the gate in the Playground.

- **`b-tabs`.**
  - `reorderable` adds drag and drop (drop on a tab's left or right half) and Ctrl+Shift+ArrowLeft/Right/Home/End from
    the keyboard. Focus stays on the moved tab, and a live region announces "label, position n of m".
  - `tab-reorder` → `{ order, tab }`. `order` gets the ids and sets them; unknown ids are ignored and missing ones kept
    after.
  - **Decided by the user:** labels are text and ids are escaped. A label's markup used to be parsed (the red run
    executed an `onerror` in a label), as `b-modal`'s title and `b-toast`'s message were before TASK-580 / TASK-597.
- **`b-split-panel`.**
  - `resizable` adds a `role="separator"` handle with aria-valuenow/min/max. Pointer drag updates live and commits on
    release; keys are ←/→ 16px, Shift 64px, Home/End to the limits, and each column stays at least 120px.
  - Each change emits `split-resize` → `{ sizes }`. `persist-key` saves to localStorage and restores on mount.
  - `sizes` gets and sets; only CSS lengths are accepted. A size reaches an inline `style`, where escaping stops an
    attribute breakout but not a second declaration, so a hostile stored value is ignored and a setter call throws.
- **Layout hook.** `captureLayout(root)` / `applyLayout(state, root)`, keyed by `layout-key`. Missing keys and invalid
  sizes are skipped.
- **Gate.** `layout-persist-smoke` 35/35. Red: the label checks on the old `b-tabs` (16 of 35 failing there). Mutation:
  restoring without validation fails the hostile-storage check. `tsc` clean on the new files; entry-points and subpath
  checks pass.
- **Full `verify.mjs`.** The same random focus and ribbon checks fail as without this suite, under load.
- **Docs.** Components README (also corrected: `tab-change` carries `{ tab }`, not `{ id }`) and CLAUDE.md.
