---
id: TASK-823
parent: EPIC-014
feature: null
status: todo
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

- [ ] Reproduced as observed state.
- [ ] Decided: does the option govern the grants admin rule too (the option's doc says "grants all access"), and is the
      `"*"` the grant-aware resolver adds for admin-grant holders the same thing or a separate marker.
- [ ] Fixed with a regression test, or the decision recorded with why it does not apply.

## Human test plan

N/A — fully covered by automated tests.
