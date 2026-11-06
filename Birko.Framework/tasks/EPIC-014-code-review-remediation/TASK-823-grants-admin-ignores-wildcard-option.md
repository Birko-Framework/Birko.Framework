---
id: TASK-823
parent: EPIC-014
feature: null
status: done
priority: P3
assignee: ai
created: 2026-11-06
depends-on: []
blocks: []
related: [TASK-176]
findings: []
pr: null
github-issue: null
jira-key: null
---

# Birko.Security.Grants.AspNetCore still treats "*" as admin when WildcardPermissionEnabled is off

## Context

Found by the `security-and-authorization` spec regen at [[TASK-176]]'s close (2026-11-06). TASK-176 made
`BirkoSecurityOptions.WildcardPermissionEnabled = false` take effect where Birko produces permissions:
`ClaimsCurrentUser` and `ResolvedPermissionsCurrentUser` leave `"*"` out of `ICurrentUser.Permissions`. The grants
layer does not read permissions through those: `UserPermissionResolverAdminSource` reads the host resolver directly, so
a resolver answering `"*"` still makes the user a grants admin, and the grant-aware resolver still adds `"*"` for
admin-grant holders. With the option off, the wildcard therefore still means "admin" in Birko.Security.Grants.AspNetCore.

Not measured yet: reproduce first (option off, a resolver answering `"*"`, check `IsAdmin` / a `RequireGrant` filter).

## Acceptance criteria

- [x] Reproduced as observed state.
- [x] Decided: does the option govern the grants admin rule too (the option's doc says "grants all access"), and is the
      `"*"` the grant-aware resolver adds for admin-grant holders the same thing or a separate marker.
- [x] Fixed with a regression test, or the decision recorded with why it does not apply.

## Human test plan

N/A — fully covered by automated tests.

## Outcome (2026-11-06)

**Reproduced** (`Task823WildcardOptionTests` against the TASK-176 code): with the option off, a host resolver answering
`"*"` made a grants admin, and an admin-grant holder's `"*"` was stripped from `ICurrentUser.Permissions` by
`ResolvedPermissionsCurrentUser`, so they were admin to the grants layer and not to `RequirePermission`. The loopback
bypass principal lost its `"*"` the same way.

**Decided** (the user, option 1): the option means "a `"*"` from the host's own permission data is not superadmin".
Birko's own grants keep it: the admin grant and the loopback bypass.

**Fixed** where the host data enters, not where it is read:
- `ClaimsCurrentUser`: `WildcardPermission.Apply(permissions, principal, options)` keeps the loopback principal's `"*"`.
- `PermissionResolutionMiddleware` applies the option to the resolver's answer (options from `RequestServices`), unless
  the resolver implements the new marker `IAppliesWildcardOption`. `ResolvedPermissionsCurrentUser` no longer filters
  and lost the `security` parameter TASK-176 gave it (unpushed, no consumer).
- `GrantAwareUserPermissionResolver` implements the marker: it strips the host's `"*"`, then adds one for the admin grant.
  `UserPermissionResolverAdminSource` ignores the host's `"*"`. `WithGrantPermissions` passes the registered options.

**Tests:** `tests/Birko.Security.Grants.Tests/AspNetCore/Task823WildcardOptionTests.cs` 2 red against the original, one
contract pin; in `Task176AuthorizationTests` the loopback and marker tests are red with each check disabled in place, and
the resolved-user test moved to the middleware. Security.AspNetCore (166) and Security.Grants (187) pass.

**Not changed:** `TenantHeaderClaimGuardMiddleware` still lets `"*"` cross tenants only with the option on, for
every source. Crossing tenants is a different power from all access within one, and the admin grant is tenant-scoped.
