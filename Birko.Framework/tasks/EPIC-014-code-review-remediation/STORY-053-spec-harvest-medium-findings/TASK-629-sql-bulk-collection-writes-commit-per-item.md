---
id: TASK-629
parent: STORY-053
feature: FEATURE-014
status: todo
priority: P2
assignee: ai
created: 2026-10-15
depends-on: []
blocks: []
findings: [SH-M028]
pr: null
github-issue: null
jira-key: null
---

# A SQL bulk collection write outside a boundary commits item by item, so a mid-batch failure leaves a partial batch

## Context

Spawned by [[TASK-170]] (2026-10-15), which confirmed SH-M028 by reading and left it unfixed because the fix changes
transaction behaviour on every provider.

`DataBaseBulkStore` / `AsyncDataBaseBulkStore` `CreateCore` / `UpdateCore` / `DeleteCore` loop over the single-entity
operation, and outside an open boundary each item commits on its own. A failure, or a cancelled token, at item k leaves
items 0..k-1 committed, and the caller cannot tell how far it got. The class doc advertises "optimized bulk operations".

**A caller can already get atomicity**: inside a `SqlUnitOfWork` (async) or a `SetTransactionContext` scope (sync) the
bulk paths join the boundary (TASK-240, `BulkTransactionBoundaryEndToEndTests`). What is missing is the default.

## What to decide

1. Should a bulk collection write outside any boundary open its own transaction for the whole batch? It is the
   conventional meaning of a bulk write, and on every provider faster than N commits; but it holds locks for the batch,
   and SQLite then takes the write lock for the whole of it.
2. The sync side has no unit of work, so the owned transaction would be the connector's.

## Acceptance criteria

- [ ] A failure at item k of a bulk create / update / delete leaves the table as it was, measured on SQLite and one server
- [ ] Inside an existing boundary, behaviour is unchanged (it joins, as today)
- [ ] Red on the current code

## Human test plan

N/A: asserted by row counts.
