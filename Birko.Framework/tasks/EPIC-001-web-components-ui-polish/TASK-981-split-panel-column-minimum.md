---
id: TASK-981
parent: EPIC-001
feature: null
status: todo
priority: P3
assignee: ai
created: 2026-11-25
depends-on: []
blocks: []
related: [TASK-979, TASK-525]
findings: []
pr: null
github-issue: null
jira-key: null
---

# `b-split-panel`: a consumer-set minimum width per column, honoured during the drag

## Context

Asked for by DraCode (its TASK-197, 2026-11-25), found while re-running TASK-979. A resizable `b-split-panel` clamps
the separator drag and the arrow keys to a fixed `MIN_COLUMN_PX = 120` per column (`b-split-panel.ts`). DraCode's chat
panel must stay at least 320 px wide, so today it lets the drag go below that and corrects the width on
`split-resize`; with `--b-split-transition` set (TASK-979) that correction animates, so a drop below the minimum
visibly springs back.

## Acceptance criteria

- [ ] New attributes `master-min` and `detail-min` (CSS lengths in px; default 120 px each, today's value) set each
      column's minimum; the pointer drag and the arrow keys clamp to them, so the column stops at its minimum while
      dragged and the drop commits a width that already respects it (nothing moves after the drop).
- [ ] Works for both shapes TASK-979 handles: a fixed master with a flexible detail, and a flexible master with a
      fixed-length detail.
- [ ] When the panel is too narrow for both minimums, the behaviour is defined and documented (e.g. the resizable
      column's own minimum wins over the other's), never a negative or NaN width.
- [ ] Doc comment, README and CHANGELOG describe the attributes.
- [ ] Playground check (extend `split-transition-check` or a sibling) proving the drag and the keys stop at a set
      minimum, red before the change.

## Out of scope

- Maximum widths.
- The responsive collapse (`collapse-at`).

## Human test plan

- [ ] In DraCode, with its chat panel's minimum set to 320 px: dragging the divider toward the chat panel stops at
      320 px while dragged, and letting go does not animate a correction.
