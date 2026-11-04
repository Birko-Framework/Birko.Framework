---
id: TASK-811
parent: STORY-065
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: done
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

- [x] Decide which layout is the one (a layout change moves existing consumer files: check Affiliate, Symbio and the
      JSON workflow and background-job stores first) and record the decision.
- [x] The sync and async separate and batch stores read and write the same layout, with a test that one reads what the
      other wrote.
- [x] If files move, existing data is still found (a read of the old layout, or a documented migration).

## Out of scope

- The XML stores, unless they show the same split.

## Human test plan

N/A — covered by automated tests.

## Decision (2026-11-04, the user)

One flat layout, `Location/`, for every JSON separate and batch store, with a tightened file match. Weighed against a
folder per store: `Location/Name` is not a layout anyone chose — it is `JsonStore.Path`, the single-file store's data
file, which the sync separate store reused as a folder (so the two collide on one settings); 4 of the 6 JSON stores and
every XML store were already flat. The flat layout's one weakness, a `{Name}-*` glob matching a store whose name extends
this one's, is removed by the match. No consumer uses these stores (Symbio, FisData, Affiliate, the other consumer repos
searched; the JSON workflow and background-job stores use the single-file `AsyncJsonStore`), so no data moves.

## Measured (2026-11-04, before the fix)

`tests/Birko.Data.JSON.Tests/Task811JsonLayoutTests.cs` and `tests/Birko.Data.XML.Tests/Task811XmlOverlapTests.cs`.

| Claim | Result |
|---|---|
| The sync separate (and batch) stores use `Location/Name/`, the async ones `Location/` | **reproduced:** the sync stores wrote under `Location/items/`; a sync and an async store on one settings counted only their own records |
| Found: in a shared folder, `{Name}-*` matches a store whose name extends this one | **reproduced** on every JSON and XML separate and batch store: `items` counted `items-archive`'s record, and its `Destroy` deleted it |

## Implementation plan

New `Birko.Helpers.RecordFiles` (`List` / `Belongs`, key rules `IsGuid` and `IsIndex` — the XML batch files carry a
batch number), used by every JSON and XML separate and batch store's file listing. The sync `JsonSeparateStore` and
`JsonBatchStore` use `PathDirectory`.

## Built (2026-11-04)

16 JSON and 8 XML cases (20 failing on the old code) and 12 `RecordFiles` cases. Inverted (rule 56): the JSON persistence
test and TASK-721's test that looked for the sync stores' files under `Location/Name`. JSON 117, XML 177, Helpers 126 and
the 8 suites importing the JSON or XML stores green; the solution builds. Spec `backend-store-implementations.md`
updated (scoped; `generated-at` left).
