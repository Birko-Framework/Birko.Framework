---
id: TASK-685
parent: EPIC-014
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: todo
priority: P2
assignee: ai
created: 2026-10-19
depends-on: []
blocks: []
# findings: ids this task remediates — from a review/audit/harvest/drill pass, or from ordinary
# field use with no pass behind it at all. Prefixes: see /tasks intake
findings: []
pr: null
github-issue: null
jira-key: null
---

# A retry after a unique violation cannot run inside a PostgreSQL transaction — the slug and default decorators

## Context

Spawned from [[TASK-680]]'s close review (2026-10-19). Measured there on live PostgreSQL 16: inside a caller's
`SqlUnitOfWork`, an INSERT the unique key refuses **aborts the transaction**, so the next statement fails with
`25P02: current transaction is aborted, commands ignored until end of transaction block` — a raw `PostgresException` in
place of the store's `StoreUniqueViolationException` (rule 9: the exception type is a contract callers select on). SQL
Server keeps the transaction and the retry succeeds inside it (measured).

TASK-680 fixed this for the translation upsert (`LocalizedTranslationRows`: a re-read that fails rethrows the original
violation, and a live test pins both providers' outcome). The same catch-then-query shape exists, **unmeasured**, in:

- `Birko.Data.Patterns/Decorators/AsyncSluggableStoreWrapper.cs` — `CreateAsync` catches `StoreUniqueViolationException`
  and calls `SlugHeldAsync`, then retries the create (and the sync twin, and the update path if it does the same);
- `Birko.Data.Patterns/Decorators/AsyncDefaultStoreWrapper.cs` around lines 98-101 (TASK-632's bounded retry).

Rule 35 is the frame: a statement the server refuses inside a transaction is a provider limit the framework must route
around. Birko.Data.SQL has no savepoints today.

## Acceptance criteria

- [ ] Measure both decorators on live PostgreSQL inside `SqlUnitOfWork` (and SQL Server for contrast): what the caller gets
- [ ] Inside an aborted transaction the caller gets the typed `StoreUniqueViolationException`, never a raw `25P02` —
      either by not querying after the refusal, or by a savepoint around the attempt (decide, write it down)
- [ ] Live tests pin the outcome per provider, red-verified

## Out of scope

- The translation upsert — fixed by [[TASK-680]].

## Human test plan

N/A — asserted by the live suites.

## Implementation plan

_Populated by `/tasks plan TASK-685` — leave empty until then._
