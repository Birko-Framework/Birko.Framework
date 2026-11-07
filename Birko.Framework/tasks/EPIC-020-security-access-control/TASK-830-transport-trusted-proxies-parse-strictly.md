---
id: TASK-830
parent: EPIC-020
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: todo
# blocked: <reason> — set by /tasks block, never by hand; status stays as it is; /tasks unblock removes it
priority: P3
# assignee — one of: agent (default), unassigned, human, or an installed agent type's name; ai is read as agent, never written
assignee: agent
created: 2026-11-07
depends-on: []
blocks: []
# findings: ids this task remediates — from a review/audit/harvest/drill pass, or from ordinary
# field use with no pass behind it at all. Prefixes: see /tasks intake
findings: []
pr: null
github-issue: null
jira-key: null
---

# Transport configurations refuse a malformed TrustedProxies entry instead of skipping it

## Context

Spawned from [[TASK-829]] (grill Q4, the user's decision on 2026-11-07). TASK-829 made `TrustedProxyList` the one parser
for every trusted-proxy list, in two modes:

- **Strict** for `AddBirkoForwardedHeaders`: a bad entry stops the host at startup.
- **Lenient** for `AuthenticationService`, which serves the `HttpListener` transports (REST, SOAP, SSE) and the
  WebSocket config. A bad entry is skipped and logged at error level. Before TASK-829 it was skipped silently.

Keeping those transports lenient held TASK-829 to its own criteria. The cost:
- An entry such as `10.0.0.300`, a typo'd CIDR, or a `${VAR}` naming an unset variable leaves the proxy untrusted.
- Every request then appears to come from the proxy, so a token binding's `AllowedIps` matches nothing, or matches
  only the proxy.
- The operator gets one error log line and no refusal.

That is rule 60's shape: the neighbouring setting (`Birko:ForwardedHeaders`) fails loudly on the same input.

Where it lives:
- `Birko.Security/Authentication/AuthenticationService.cs`, constructor: the `TrustedProxyList.ParseLenient` call.
- Static `GetClientIpAddress(..., IReadOnlyCollection<string>)`: public, and parses per call.

## Acceptance criteria

- [ ] `AuthenticationService` parses `TrustedProxies` with `TrustedProxyList.Parse`. A bad entry throws
      `InvalidOperationException` at construction, naming the entry and the configuration type, and wrapping the
      `FormatException`.
- [ ] Decide with the user what the public static `GetClientIpAddress(…, IReadOnlyCollection<string>)` does with a bad
      entry (it parses per request), and record it.
- [ ] Each transport's startup path (REST server, SOAP, SSE, WebSocket) is checked to surface the throw at startup,
      not on the first request. Any that does not is listed with what it does instead.
- [ ] Tests in `tests/Birko.Security.Tests` pin the refusal. The transport suites that import Birko.Security stay
      green.
- [ ] `Birko.Security/CLAUDE.md` and `docs/security.md` § Static Token Authentication say a bad entry refuses. A
      Recent Updates entry marks it **breaking**: a config that started before may now refuse.

## Out of scope

- The forwarded-headers registration, already strict since TASK-829.

## Human test plan

N/A — fully covered by automated tests: the change is construction-time parsing, asserted directly. A person running a
transport with a bad entry would see only the exception the tests already check.

## Implementation plan
