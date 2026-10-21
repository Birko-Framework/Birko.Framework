---
id: TASK-721
parent: STORY-065
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: todo
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

- [ ] Reproduce each claim (a test or a live measurement) and record the result; a claim that does not reproduce is
      closed with its evidence, never quietly dropped (rule 54).
- [ ] Each reproduced defect fixed, with a regression test that fails without the fix.
- [ ] The spec area that describes the behaviour updated to the fixed behaviour.

## Out of scope

- Findings of other tasks in EPIC-023.

## Human test plan

N/A — covered by automated tests once reproduced.

## Implementation plan

_Populated by `/tasks plan TASK-721` — leave empty until then._
