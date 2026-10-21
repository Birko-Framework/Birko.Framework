---
id: TASK-713
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
findings: [SH-T696-08]
pr: null
github-issue: null
jira-key: null
---

# Event-sourcing wrappers: event before write, no whole-table guard, per-caller state on the wrapper

## Context

Suspected by the TASK-696 spec harvest (22a2bb77), read from the code and **not measured**. The event is appended before the inner write, so a failed write leaves an orphan event and a bumped `Version`; `EventData` carries the old `Version`. The filtered update/delete have no null or whole-table guard (rules 1-3). Items with a null Guid get no event but are still written. `CurrentUserGuid` is settable state on a possibly shared wrapper (rules 36/62), and the builder never sets it.

Evidence: Birko.Data.EventSourcing/AsyncEventSourcingStoreWrapper.cs:34,72-80,141-147; AsyncEventSourcingBulkStoreWrapper.cs:107,168,194-253; StoreWrapperBuilder.cs:54.

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

_Populated by `/tasks plan TASK-713` — leave empty until then._
