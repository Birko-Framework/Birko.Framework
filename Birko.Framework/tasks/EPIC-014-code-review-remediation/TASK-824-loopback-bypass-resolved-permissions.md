---
id: TASK-824
parent: EPIC-014
feature: null
status: done
priority: P3
assignee: ai
created: 2026-11-06
depends-on: []
blocks: []
related: [TASK-823, TASK-176]
findings: []
pr: null
github-issue: null
jira-key: null
---

# The loopback bypass grants nothing under UseResolvedPermissions

## Context

Found by the `security-and-authorization` respec at [[TASK-823]] (2026-11-06), from reading the code; not measured.
`LoopbackBypassMiddleware` runs an unauthenticated loopback request as a principal holding a `"*"` permission claim and
user id `Guid.Empty`. Under `UseResolvedPermissions`, `ICurrentUser` is `ResolvedPermissionsCurrentUser`, which reads only
what `PermissionResolutionMiddleware` stored: that middleware sees an authenticated principal, asks the host resolver
about `Guid.Empty`, and the claim's `"*"` is never read. So the bypass grants whatever the host answers for an empty id,
usually nothing, whatever `WildcardPermissionEnabled` says. Predates TASK-176 and TASK-823.

## Acceptance criteria

- [x] Reproduced as observed state (loopback bypass + `UseResolvedPermissions`, a `RequirePermission` endpoint).
- [x] Fixed with a regression test (e.g. the middleware stores `{"*"}` for the bypass principal without asking the
      resolver), or the decision recorded with why the bypass should not apply on that path.

## Human test plan

N/A — fully covered by automated tests.

## Outcome (2026-11-07)

Reproduced first (`Task824LoopbackResolvedPermissionsTests`). A real loopback Kestrel host with the bypass,
`UseResolvedPermissions`, a resolver answering nothing and a `RequirePermission("runs:read")` endpoint answered a
token-less request **403**, with the wildcard option on and off. The resolver had been asked about `Guid.Empty`.

The fix is in `PermissionResolutionMiddleware`. The bypass principal (authentication type `LoopbackBypass`, the same test
`WildcardPermission.Apply` uses since TASK-823) is not resolved. Its own permission claims (`"*"`) are stored, with no
roles, and the wildcard option is not applied, because TASK-823 decided the bypass keeps its `"*"`. Afterwards: 200 with
`"*"` in both modes, and the resolver is never asked about `Guid.Empty`. Red against the original, 2 of 2.

Regression: Security.AspNetCore 168 and Security.Grants 187, all green.
