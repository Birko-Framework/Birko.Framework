---
id: STORY-070
parent: EPIC-001
feature: FEATURE-022
status: todo
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

When each part lands, tell the DraCode session which Birko.Web version it is in.

## Tasks

- TASK-946 — b-kanban: buttons, links and fields on a card do not start a drag or count as a card click
- TASK-947 — b-kanban: locked columns, a move rule, and refusing or undoing a move
- TASK-948 — b-kanban: lanes inside a column
- TASK-949 — b-kanban: reordering off per column, and the keyboard across lanes
- TASK-950 — b-kanban: an opt-in stacked layout for a narrow board
