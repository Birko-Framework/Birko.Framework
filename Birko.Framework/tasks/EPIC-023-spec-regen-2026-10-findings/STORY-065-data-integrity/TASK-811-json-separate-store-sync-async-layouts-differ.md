---
id: TASK-811
parent: STORY-065
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: todo
# blocked: <reason> — add this line while the task is blocked, keeping its status; /tasks unblock removes it
priority: P2
assignee: ai
created: 2026-11-04
depends-on: []
blocks: []
# findings: ids this task remediates — from a review/audit/harvest/drill pass, or from ordinary
# field use with no pass behind it at all. Prefixes: see /tasks intake
findings: []
pr: null
github-issue: null
jira-key: null
---

# JSON separate stores: the sync and async stores keep their files in different directories

## Context

Found while doing TASK-721 (2026-11-04). On `new Settings(location, "items")`, the sync `JsonSeparateStore` (and
`JsonBatchStore`, which derives from it) writes `location/items/items-{guid}`, treating `Path` (Location joined with Name)
as its directory. `AsyncJsonSeparateStore` and `AsyncJsonBatchStore` write `location/items-{guid}`, using
`PathDirectory`. The bulk variants of both use `PathDirectory`. So a sync and an async separate store on the same
settings do not see each other's data, and a consumer moving from one to the other finds its store empty. Measured:
TASK-721's tests had to look for the async stores' files in `Location` and the sync stores' in `Location/Name`.
TASK-504 records the sync layout as intended for `JsonSeparateStore` ("Location/Name is its own directory").

Evidence: Birko.Data.JSON/Stores/JsonSeparateStore.cs (DestroyCore, SaveData use `Path`);
AsyncJsonSeparateStore.cs (InitCoreAsync, LoadDataAsync, SaveDataAsync use `PathDirectory`).

## Acceptance criteria

- [ ] Decide which layout is the one (a layout change moves existing consumer files: check Affiliate, Symbio and the
      JSON workflow and background-job stores first) and record the decision.
- [ ] The sync and async separate and batch stores read and write the same layout, with a test that one reads what the
      other wrote.
- [ ] If files move, existing data is still found (a read of the old layout, or a documented migration).

## Out of scope

- The XML stores, unless they show the same split.

## Human test plan

N/A — covered by automated tests.

## Implementation plan

_Populated by `/tasks plan TASK-811` — leave empty until then._
