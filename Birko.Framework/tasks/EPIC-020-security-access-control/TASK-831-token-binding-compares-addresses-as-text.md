---
id: TASK-831
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

# A token binding's AllowedIps match an IPv4-mapped client address

## Context

Found while regenerating the `security-and-authorization` spec after [[TASK-829]] (2026-11-07). The user chose to
file it as its own task. It is read from the code and has not been measured.

- `AuthenticationService.ValidateToken(token, clientIp)` compares a binding's expanded `AllowedIps` with the client
  address **as text**.
- Several paths hand it the connection's own address unchanged, and on a dual-stack socket that address is
  IPv4-mapped (`::ffff:203.0.113.7`):
  - the untrusted-peer fallback of `GetClientIpAddress`;
  - the TASK-829 WebSocket short-circuit, which returns `Connection.RemoteIpAddress.ToString()`.
- A binding listing `203.0.113.7` then refuses a client that is `203.0.113.7`. The trusted-proxy walk already
  normalises mapped addresses (`Normalize`), so only these paths are affected.
- This predates TASK-829. It is not a regression.

## Acceptance criteria

- [ ] Measure it first: a binding with `AllowedIps = ["203.0.113.7"]` and client `::ffff:203.0.113.7`. Record the
      result here. If it is accepted already, close this task as not-a-bug with the measurement.
- [ ] Binding addresses and the client address compare as parsed IPs, with IPv4-mapped reduced to IPv4. An entry
      that is not an address is skipped and logged, as now.
- [ ] Tests in `tests/Birko.Security.Tests` cover the mapped client against an IPv4 entry, the reverse, and an IPv6
      entry. A WebSocket test does the same through the short-circuit.

## Out of scope

- Strict parsing of `TrustedProxies` (TASK-830).

## Human test plan

N/A — fully covered by automated tests: the comparison is pure address logic, asserted directly.

## Implementation plan
