---
id: TASK-710
parent: STORY-064
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
findings: [SH-T696-05]
pr: null
github-issue: null
jira-key: null
---

# SQL outbox: a stale reclaim can be won twice, and marks ignore the claim token

## Context

Suspected by the TASK-696 spec harvest (22a2bb77), read from the code and **not measured**. The reclaim's conditional update tests `Status == origin`, which is still true after the first processor's reclaim, so two processors reclaiming one stale row can both win and both publish. `MarkPublishedAsync` / `MarkFailedAsync` do not check `ClaimToken`, so a slow processor whose row was reclaimed overwrites it. The reclaim cap is the store's own `maxAttempts`, separate from `OutboxOptions.MaxAttempts`; the class comment still says a claim is not a lease (contradicted by TASK-451).

Evidence: Birko.EventBus.Outbox.SQL/SqlOutboxStore.cs:40,110-146,167,188.

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

_Populated by `/tasks plan TASK-710` — leave empty until then._
