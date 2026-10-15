---
id: TASK-625
parent: EPIC-014
feature: null
status: done
priority: P3
assignee: ai
created: 2026-10-15
depends-on: [TASK-567]
blocks: []
related: [TASK-567, TASK-163]
findings: []
pr: null
github-issue: null
jira-key: null
---

# A duplicate create is refused with five different exception types across the providers

## Context

Measured 2026-10-15 for TASK-567 with the shared contract's duplicate-create tests, on every provider live. After
TASK-567, every provider refuses a create of a stored Guid and keeps the stored entity. The **type** still differs:

| Provider | Throws |
|---|---|
| InMemory, SQL (5), RavenDB, and the JSON/XML paths TASK-567 fixed | `StoreException` |
| MongoDB | `MongoWriteException` / `MongoBulkWriteException` |
| CosmosDB | `CosmosException` (409) |
| ElasticSearch | `InvalidOperationException` |
| JSON (single), XML store (single) | `ArgumentException` |

Code written against `IStore` cannot catch "already exists" portably. Rule 9: an exception's type is a contract that
filters select on, so **enumerate the catch sites of each type before replacing it**. A rewrap that breaks a working
`catch (MongoWriteException)` is a regression of its own.

## Acceptance criteria

- [x] Every catch site of the five types in the framework and in Symbio is listed here, with whether a rewrap changes
      its behaviour.
      → Searched `Framework/` and `Consumers/` (Symbio, DraCode, Sandbox), excluding tests, for catches of
      `MongoWriteException`, `MongoBulkWriteException`, `CosmosException`, and broad `ArgumentException` /
      `InvalidOperationException` catches, plus duplicate detection by code (`11000`, `DuplicateKey`, `Conflict`, 409).
      **None surrounds a store create.** Every `CosmosException` catch selects `NotFound` on a read or delete (stores,
      views, migrations). The `InvalidOperationException` / `ArgumentException` catches are unrelated (parsers,
      sockets, ES index info, contrast). The one duplicate-sensitive consumer is `SqlEventStore`, which TASK-567
      already covered. So the rewrap changes no existing behaviour.
- [x] Each provider's duplicate create throws `StoreException` (the provider's exception as `InnerException`), sync
      and async, single and bulk. Only the "already exists" case is rewrapped; other failures keep their types.
      → One translation in the base stores' public create wrappers (rule 12), driven by
      `protected virtual bool IsDuplicateKeyException(Exception)`, which MongoDB (duplicate-key write category) and
      CosmosDB (409) override. ElasticSearch types it in the provider beside its `ThrowIfOnlyMissingDocuments`
      precedent (a 409 on single create, `ThrowIfOnlyConflicts` on bulk). JSON/XML single create uses TASK-567's
      `StoredKeyGuard`. One factory: `StoreException.DuplicateCreate(type, inner)`. **A refused bulk create is not
      atomic on MongoDB (ordered `InsertMany`), CosmosDB (parallel creates) or ElasticSearch (bulk): items before or
      beside the duplicate are stored.** It is atomic on SQL (transaction), JSON/XML and RavenDB (per 512 chunk).
- [x] The shared contract's duplicate-create tests are tightened from `Throw<Exception>` to `Throw<StoreException>`
      (TASK-567 left them loose deliberately), and pass on all 12 providers live.
      → `DuplicateCreateThrowsStoreException` and its ten overrides are deleted; the contract asserts `StoreException`.

## Out of scope

- Whether duplicates are refused at all (TASK-567).

## Human test plan

N/A — covered by the shared contract tests.

## Implementation plan

1. The catch-site audit (above).
2. Base wrappers plus `IsDuplicateKeyException` (MongoDB, CosmosDB); ES typed in the provider; JSON/XML single through
   the guard; `StoreException.DuplicateCreate`.
3. Delete the TASK-567 switch, tighten the contract, run it on all 12 providers live, and run every store-dependent
   suite.

## Progress log

- 2026-10-15 — Audit clean (above). Implemented, then ran the strict contract live on all 12 providers: 11 passed first
  time. The ES sync single create still threw `InvalidOperationException`, because a broad `catch (Exception)` in
  `CreateCore` rewrapped the new `StoreException`; fixed. Then every test project that imports `Birko.Data.Stores`:
  113 projects, 6,143 tests, all green live (8 servers).
