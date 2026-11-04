---
id: TASK-713
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

- [x] Reproduce each claim (a test or a live measurement) and record the result; a claim that does not reproduce is
      closed with its evidence, never quietly dropped (rule 54).
- [x] Each reproduced defect fixed, with a regression test that fails without the fix.
- [x] The spec area that describes the behaviour updated to the fixed behaviour.

## Out of scope

- Findings of other tasks in EPIC-023.

## Human test plan

N/A — covered by automated tests once reproduced.

## Measured (2026-11-04, before the fix)

`tests/Birko.Data.EventSourcing.Tests/Task713EventSourcingGuardTests.cs` and
`tests/Birko.Data.Composition.Tests/Task713BuilderEventUserTests.cs` against the shipped wrappers, sync and async.

| Claim | Result |
|---|---|
| The event is appended before the inner write, so a failed write leaves an orphan event | **true, and documented as the design** (CLAUDE.md, README, spec: atomicity is the event-store backend's). Not changed here; recorded |
| `EventData` carries the old `Version` | **reproduced:** events 1..4 carried payload versions 0..3 |
| The filtered update/delete have no null or whole-table guard | **reproduced:** a null filter and `x => !empty.Contains(x.Name)` rewrote or deleted every row, an event each |
| Items with a null Guid get no event but are still written | **reproduced on update:** the stored item's event was appended, then the inner store refused the call (orphan event) |
| `CurrentUserGuid` is settable state on a shared wrapper; the builder never sets it | **reproduced:** every event through the builder had `UserGuid == null` |

## Implementation plan

One event producer per base wrapper, `NewEvent(item, type, version)`: sets `Version`, then serializes, user from a
`Func<Guid?>? currentUser` constructor parameter read at each write; `CurrentUserGuid` is removed (rule 36: no one set
it; grepped Birko\Consumers). The builder passes `() => auditContext.CurrentUserGuid`. The filtered writes call
`BoundedFilterGuard` after a null check (rules 1-3). A bulk update with an item without a Guid throws
`EntityNotFoundException` before any event; a bulk delete leaves such items out.

## Built (2026-11-04)

17 tests: 14 of the 15 wrapper tests run on the old API and all fail there (the user test needs the new
constructor); of the builder's two, the user test fails on the old code and the no-context one passes. **Breaking:** `CurrentUserGuid` is gone; pass `currentUser`. EventSourcing 22,
EventSourcing.SQL 26, Composition 28, EventBus 133 green. Spec `store-decorator-composition.md` updated (scoped;
`generated-at` left).
