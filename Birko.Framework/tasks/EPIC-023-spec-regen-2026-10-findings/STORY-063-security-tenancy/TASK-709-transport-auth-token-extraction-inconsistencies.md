---
id: TASK-709
parent: STORY-063
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
findings: [SH-T696-04]
pr: null
github-issue: null
jira-key: null
---

# Transport auth adapters extract tokens inconsistently

## Context

Suspected by the TASK-696 spec harvest (22a2bb77), read from the code and **not measured**. SSE: an `Authorization` header with another scheme returns a null token and the query string is never tried. REST: a non-Bearer `Authorization` value (`Basic …`) is passed through whole as the token. The query-token name is case-sensitive in SOAP and case-insensitive in SSE. `SoapAuthenticationService` has `Dispose()` but does not implement `IDisposable`. A stale comment in `GrantAwareUserPermissionResolver` still describes the pre-TASK-641 refusal.

Evidence: SseAuthenticationService.cs:150-153,190; RestAuthenticationService.cs:62; SoapAuthenticationService.cs:11,104,142; GrantAwareUserPermissionResolver.cs:26.

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

_Populated by `/tasks plan TASK-709` — leave empty until then._
