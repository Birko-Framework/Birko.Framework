---
id: TASK-714
parent: STORY-064
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: todo
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

- [ ] Reproduce each claim (a test or a live measurement) and record the result; a claim that does not reproduce is
      closed with its evidence, never quietly dropped (rule 54).
- [ ] Each reproduced defect fixed, with a regression test that fails without the fix.
- [ ] The spec area that describes the behaviour updated to the fixed behaviour.

## Out of scope

- Findings of other tasks in EPIC-023.

## Human test plan

N/A — covered by automated tests once reproduced.

## Implementation plan

_Populated by `/tasks plan TASK-714` — leave empty until then._
