---
id: TASK-709
parent: STORY-063
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

- [x] Reproduce each claim (a test or a live measurement) and record the result; a claim that does not reproduce is
      closed with its evidence, never quietly dropped (rule 54).
- [x] Each reproduced defect fixed, with a regression test that fails without the fix.
- [x] The spec area that describes the behaviour updated to the fixed behaviour.

## Out of scope

- Findings of other tasks in EPIC-023.

## Human test plan

N/A — covered by automated tests once reproduced.

## Measured (2026-11-04, before the fix)

Tests through each adapter's public API.

| Claim | Result |
|---|---|
| SSE: an `Authorization` header with another scheme gives a null token and the query string is never tried | **reproduced:** `Authorization: Basic …` with `?token=good` was refused |
| REST: a non-Bearer `Authorization` value is passed whole as the token | **reproduced:** `"Basic dXNlcjpwYXNz"` came back as the token, and the API-key header and query token after it were never read |
| The query-token name is case-sensitive in SOAP and case-insensitive in SSE | **reproduced:** SSE took `?Token=`; SOAP and REST (its parsed query is an ordinal dictionary) do not |
| `SoapAuthenticationService` has `Dispose()` but is not `IDisposable` | **reproduced** |
| A stale comment in `GrantAwareUserPermissionResolver` | **reproduced:** it said `IGrantService` refuses a retired role loudly; since TASK-641 a stored one grants nothing |

## Implementation plan

One producer for both rules on `AuthenticationService` (Birko.Security): `TokenFromAuthorizationHeader(value, scheme)`
returns the token under the scheme or null, and the adapters go on to their next source on null;
`TokenFromQueryString(query, name)` matches the name exactly, as two of the three transports already did. No consumer
uses these adapters (grepped Birko\Consumers), so REST no longer taking a raw `Authorization` value breaks nobody.

## Built (2026-11-04)

`Task709SseTokenExtractionTests` (3), `Task709RestTokenExtractionTests` (3), `Task709SoapAuthenticationTests` (2),
`Birko.Security.Tests/Task709TokenExtractionTests` (22 cases); 6 of the adapter tests fail on the old code (the two
that pass pin a bearer token still being read). Security 121, SSE 28, SSE.AspNetCore 8, REST.Server 10, SOAP 9,
WebSocket 92 green. Spec `security-and-authorization.md` updated (scoped; `generated-at` left).
