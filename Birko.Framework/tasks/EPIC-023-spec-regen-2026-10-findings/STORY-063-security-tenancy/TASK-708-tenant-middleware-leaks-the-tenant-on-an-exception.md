---
id: TASK-708
parent: STORY-063
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
findings: [SH-T696-03]
pr: null
github-issue: null
jira-key: null
---

# Data.Tenant's middleware leaves the tenant set when a later step throws

## Context

Suspected by the TASK-696 spec harvest (22a2bb77), read from the code and **not measured**. `TenantMiddleware` calls `ClearTenant()` after `await _next(context)` with no `try`/`finally`, so an exception downstream leaves the tenant set, which leaks it when the context is a singleton or `Tenant.Current`. The Security.AspNetCore middleware clears in a `finally`. Also: the Purpose prose of the tenant spec says a missing tenant is a 400, the middleware returns 401.

Evidence: Birko.Data.Tenant/Middleware/TenantMiddleware.cs:96,109-112.

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

_Populated by `/tasks plan TASK-708` — leave empty until then._
