---
id: STORY-070
parent: EPIC-001
feature: FEATURE-022
status: in-progress
created: 2026-11-21
---

# `b-kanban`: move rules, lanes, a narrow layout, and buttons on cards

## User story

As an app that builds a board on `b-kanban` (DraCode's project Board first), I want locked columns, my own move rule,
lanes inside a column, an opt-in stacked layout for a narrow board, and card buttons that don't start a drag, so I can
use the component instead of hand-built columns.

## Behaviour

The decisions are FEATURE-022's ([decisions.md](../../../docs/features/FEATURE-022-kanban-move-rules-lanes-narrow-layout/decisions.md)).
TASK-946 stands alone and can ship first. TASK-947 (locks, the move rule, refuse and undo) comes before TASK-948
(lanes), which TASK-949 (reordering off, keyboard across lanes) and TASK-950 (stacked layout) build on.

**One release (the user, 2026-11-21: "all kanban should be done in one tag").** Every task here is built on one
Birko.Web integration branch, `task/TASK-947` (worktree `C:\Source\Birko\Web-TASK-947`), as v0.4.21, and merged into
Web main and tagged once, when all of them are built. Web main, which Symbio's dev server builds from, stays unchanged
until then. TASK-955 (keyboard reach, found testing TASK-947) rides the same branch. When the release lands, tell the
DraCode session the version.

## Tasks

- TASK-946 — b-kanban: buttons, links and fields on a card do not start a drag or count as a card click
- TASK-947 — b-kanban: locked columns, a move rule, and refusing or undoing a move
- TASK-948 — b-kanban: lanes inside a column
- TASK-949 — b-kanban: reordering off per column, and the keyboard across lanes
- TASK-950 — b-kanban: an opt-in stacked layout for a narrow board
- TASK-956 — b-kanban: lanes shown as rows across the board (swimlanes)
- TASK-957 — b-kanban: lanes shown as a tag on each card
- TASK-955 — b-kanban cards cannot be reached with the keyboard (found testing TASK-947; parented to EPIC-001, shipped here)
