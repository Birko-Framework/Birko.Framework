---
id: TASK-500
parent: EPIC-014
feature: null
status: todo
priority: P3
assignee: ai
created: 2026-09-26
depends-on: []
blocks: []
related: [TASK-498]
findings: [FIELD-013]
pr: null
github-issue: null
jira-key: null
affects: [Birko.Data.Patterns]
---

# The Timestamp and Audit bulk decorators write into the caller's `PropertyUpdate`

Found while working TASK-498 (planning pass), pre-existing, not introduced by it.

## Context

`Birko.Data.Patterns/Decorators/{Async,}TimestampBulkStoreWrapper.cs` (`UpdateAsync(filter, PropertyUpdate<T>)`)
calls `updates.Set(x => x.UpdatedAt, _clock.UtcNow)` on the object the caller passed in, and
`{Async,}AuditBulkStoreWrapper.cs` does the same with `UpdatedBy`. That is rule 27's first half — *a decorator never
writes to an object it did not create* — applied to an argument rather than a returned entity.

Consequences:
- A caller who reuses one `PropertyUpdate` for two updates stamps `UpdatedAt` twice; since TASK-498 an update that
  already carries an `Increment` on that member would now throw instead (not reachable for a `DateTime`, but the
  shape is the same for any future numeric stamp).
- Two decorators in one stack both append, and whatever the caller inspects afterwards is not what they built.

## Acceptance criteria

- [ ] Timestamp and Audit bulk decorators (sync and async) send the inner store a copy of the caller's update plus
      their own Set; the caller's `PropertyUpdate` is unchanged after the call.
- [ ] A regression test per decorator asserts the caller's `Assignments` count is unchanged after `Update`, and that
      the stamp still lands.
- [ ] `PropertyUpdate<T>` gains whatever copy it needs without exposing `Assignments` publicly.

## Out of scope

- The single-entity (non-bulk) Timestamp/Audit wrappers, which stamp the entity — covered by rule 27's own history.

## Implementation plan

_Populated by `/tasks plan TASK-500` — leave empty until then._
