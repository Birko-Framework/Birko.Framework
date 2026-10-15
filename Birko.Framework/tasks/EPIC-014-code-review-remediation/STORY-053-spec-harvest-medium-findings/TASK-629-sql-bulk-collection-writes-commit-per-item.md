---
id: TASK-629
parent: STORY-053
feature: FEATURE-014
status: done
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

- [x] A failure at item k of a bulk create / update / delete leaves the table as it was, measured on SQLite and one server
- [x] Inside an existing boundary, behaviour is unchanged (it joins, as today)
- [x] Red on the current code

## Resolution (2026-10-15)

**Decided: a bulk collection write outside any boundary is one transaction.** It is the conventional meaning of a bulk
write, it is what the class doc advertised, and on every provider one commit is cheaper than N. The lock cost is the
batch's duration, which is the caller's choice of batch size. Inside an existing boundary (an ambient transaction for
the database, or the store's `SetTransactionContext`) it joins, unchanged.

`DataBaseBulkStore.Atomically` / `AsyncDataBaseBulkStore.AtomicallyAsync` wrap the public collection `Create` / `Update`
/ `Delete` (not the overridable `*Core`, so a provider override is covered, rule 12). With no boundary they open a
connection and a transaction and publish it as the ambient scope, so each per-item write and a provider's native bulk
insert join it (TASK-240). Any failure rolls back, a cancelled token included (the rollback does not use the caller's
token). One exception commits: TASK-555's `EntityNotFoundException`, raised after every stored item was written, is the
collection update's contract, and a zero-row UPDATE does not abort the transaction (measured on PostgreSQL, where a
failed statement would).

**Red-verified.** `AtomicBulkWriteTests` (SQLite) 3 of 5 (pins: TASK-555's partial update, a successful batch);
`AtomicBulkWriteLiveTests` 2 of 3 on PostgreSQL, SQL Server and MySQL each (pin: TASK-555). The existing
`BulkTransactionBoundaryEndToEndTests` (joining a boundary) pass unchanged.

**Spec regen pending**, batched: `store-crud-contract`.

## Human test plan

N/A: asserted by row counts.
