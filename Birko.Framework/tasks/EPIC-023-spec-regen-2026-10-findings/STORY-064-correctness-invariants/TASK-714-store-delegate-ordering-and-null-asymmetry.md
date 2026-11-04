---
id: TASK-714
parent: STORY-064
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: done
# blocked: <reason> — add this line while the task is blocked, keeping its status; /tasks unblock removes it
priority: P2
assignee: ai
created: 2026-10-21
depends-on: []
blocks: []
# findings: ids this task remediates — from a review/audit/harvest/drill pass, or from ordinary
# field use with no pass behind it at all. Prefixes: see /tasks intake
findings: [SH-T696-09]
pr: null
github-issue: null
jira-key: null
---

# Store contract: delegate ordering and sync/async null handling

## Context

Suspected by the TASK-696 spec harvest (22a2bb77), read from the code and **not measured**. The SQL update builds its WHERE clause before the store delegate runs, so a delegate returning a copy with its own Guid rewrites the original row's key. The in-memory bulk create checks keys before the delegate, so a replacement carrying a duplicate Guid fails after earlier items were stored, and `EnsureGuid` changes the caller's entities even on refusal. Sync `CreateCore`/`UpdateCore` throw NRE on null where the async twins return quietly; SQL bulk create NREs on a null element in every SQL store. `GetUnwrappedStore` has no cycle guard.

Evidence: Birko.Data.SQL/Stores/DataBaseStore.cs:218,249-254; AsyncDataBaseStore.cs:283,339; Birko.Data.InMemory/Stores/AbstractInMemoryStore.cs:201-207; AsyncSQLiteStore.cs:115; StoreExtensions.cs:24.

## Acceptance criteria

- [x] Reproduce each claim (a test or a live measurement) and record the result; a claim that does not reproduce is
      closed with its evidence, never quietly dropped (rule 54).
- [x] Each reproduced defect fixed, with a regression test that fails without the fix.
- [x] The spec area that describes the behaviour updated to the fixed behaviour.

## Out of scope

- Findings of other tasks in EPIC-023.

## Human test plan

N/A — covered by automated tests once reproduced.

## Measured (2026-11-04, before the fix)

`tests/Birko.Data.SQL.SqLite.Tests/Task714StoreContractTests.cs` and
`tests/Birko.Data.InMemory.Tests/Task714InMemoryContractTests.cs` against the shipped stores.

| Claim | Result |
|---|---|
| A SQL update whose delegate returns a copy with its own Guid rewrites the original row's key | **reproduced**, sync and async: no exception, and the in-memory store wrote the copy's row instead |
| The in-memory bulk create checks keys before the delegate, so a duplicate replacement fails after earlier items were stored | **reproduced:** 2 rows after the refused batch (1 expected), sync and async |
| `EnsureGuid` changes the caller's entities even on refusal | **reproduced:** a refused batch left a Guid on the caller's new entity |
| Sync `Create`/`Update` throw NRE on null where the async twins return quietly | **reproduced:** async create answered `Guid.Empty` and async update returned |
| SQL bulk create NREs on a null element | **reproduced** on SQLite, create and update |
| `GetUnwrappedStore` has no cycle guard | **reproduced:** the test run hung until the 300 s timeout |

## Implementation plan

`StoreDelegates.Apply`, the one producer every store calls, refuses a returned entity with another key; an
identity-less copy still inherits the key. The public `Create` / `Update` (sync and async) refuse `null` with
`ArgumentNullException` on every provider; the SQL cores' own null answers go (rule 53). Every SQL bulk create and update
(the eight provider stores, TimescaleDB's two, the base loops) skips a null element, as every portable store does. The
in-memory bulk create checks the keys that exist first, then assigns Guids, runs every delegate, and only then stores.
`GetUnwrappedStore` keeps the stores it visited and refuses a repeat.

## Built (2026-11-04)

10 tests: 9 fail on the old code (the cycle test by hanging); the tenth pins that an identity-less copy still
inherits the key. No consumer delegate returns another entity (grepped Birko\Consumers: every `StoreDataDelegate` use
passes through). Two CosmosDB tests pinned the quiet `Create(null)` → `Guid.Empty` and are inverted (rule 56). All 124
suites importing Birko.Data.Stores green, TimescaleDB re-run after its two bulk sites. Spec
`store-crud-contract.md` and `backend-store-implementations.md` updated (scoped; `generated-at` left).
