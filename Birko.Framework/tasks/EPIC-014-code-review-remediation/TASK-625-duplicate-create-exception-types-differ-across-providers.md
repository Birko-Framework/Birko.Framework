---
id: TASK-625
parent: EPIC-014
feature: null
status: todo
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

- [ ] Every catch site of the five types in the framework and in Symbio is listed here, with whether a rewrap changes
      its behaviour.
- [ ] Each provider's duplicate create throws `StoreException` (the provider's exception as `InnerException`), sync
      and async, single and bulk. Only the "already exists" case is rewrapped; other failures keep their types.
- [ ] The shared contract's duplicate-create tests are tightened from `Throw<Exception>` to `Throw<StoreException>`
      (TASK-567 left them loose deliberately), and pass on all 12 providers live.

## Out of scope

- Whether duplicates are refused at all (TASK-567).

## Human test plan

N/A — covered by the shared contract tests.

## Implementation plan

_Drafted when picked._
