---
id: TASK-721
parent: STORY-065
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: done
# blocked: <reason> — add this line while the task is blocked, keeping its status; /tasks unblock removes it
priority: P1
assignee: ai
created: 2026-10-21
depends-on: []
blocks: []
# findings: ids this task remediates — from a review/audit/harvest/drill pass, or from ordinary
# field use with no pass behind it at all. Prefixes: see /tasks intake
findings: [SH-T696-16]
pr: null
github-issue: null
jira-key: null
---

# JSON stores: Destroy deletes the whole directory, save is not atomic, loaders fail on a null record

## Context

Suspected by the TASK-696 spec harvest (22a2bb77), read from the code and **not measured**. `JsonSeparateBulkStore.Destroy` deletes the whole `Location` directory, losing anything else in it. JSON save is delete-then-write, not atomic (XML writes a temp file and moves it over). The async JSON/XML loaders throw NRE on a null record. The async JSON store reloads from disk whenever it is empty.

Evidence: Birko.Data.JSON/Stores/JsonSeparateBulkStore.cs:69; JsonStore.cs:249; AsyncJsonStore.cs:246,264; XmlStore.cs:265; AsyncXmlStore.cs:270; AbstractAsyncJsonStore.cs:134.

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

Tests written against the shipped stores (`Task721JsonStoreSafetyTests`, `Task721XmlStoreSafetyTests`).

| Claim | Result |
|---|---|
| `JsonSeparateBulkStore.Destroy` deletes the whole `Location` directory, losing anything else | **not as stated:** `Directory.Delete` is not recursive, so nothing foreign was lost; instead Destroy deleted the store's files and then **threw `IOException`** ("directory is not empty"). The same in all eight JSON separate and batch stores and both XML separate stores |
| JSON save is delete-then-write, not atomic | **reproduced**, worse than the single file: a failed write left an **empty file** on the single, separate and batch JSON stores, sync and async. Also found: the XML separate stores' update deleted the entity's file before writing, and the XML batch saves deleted **every** batch file before writing any (a failed second batch lost both) |
| The async JSON/XML loaders throw NRE on a null record | **reproduced** on the async JSON, both async JSON batch, the sync and async single-file XML and both XML batch loaders. The sync JSON loaders already skipped one (CR-L131) |
| The async JSON store reloads from disk whenever it is empty | **reproduced:** 4 disk reads for 3 calls on an empty store (XML fixed this as CR-M182) |

Also found: the sync and async JSON separate stores keep their files in different directories (TASK-811).

## Implementation plan

One producer for the atomic write, `Birko.Helpers.AtomicFile` (temp beside the target, then move), used by every JSON
and XML write, the single-file XML stores' own copy included. Destroy deletes the store's files, then the directory only
if empty (TASK-504's removal kept when nothing else is there). Every loader skips a null record. The async JSON stores
get XML's `_loaded` flag, reset by Destroy. The XML batch save writes the batches first and deletes the stale ones
after.

## Built (2026-11-04)

`tests/Birko.Data.JSON.Tests/Task721JsonStoreSafetyTests.cs` (19), `tests/Birko.Data.XML.Tests/Task721XmlStoreSafetyTests.cs`
(10), `tests/Birko.Helpers.Tests/AtomicFileTests.cs` (6). Against the old stores 17 of the 19 JSON tests fail (the
two that pass guard the sync null skip and TASK-504's directory removal) and all 10 XML tests. A batch save spanning
several files is atomic per file, not across them: a failure part way can leave new and old batches side by side.
11 importing suites green. Spec `backend-store-implementations.md` updated (scoped; `generated-at` left).
