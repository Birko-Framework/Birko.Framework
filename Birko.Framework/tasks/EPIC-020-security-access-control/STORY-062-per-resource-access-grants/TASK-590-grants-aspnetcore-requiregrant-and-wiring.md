---
id: TASK-590
parent: STORY-062
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: todo
# blocked: <reason> — add this line while the task is blocked, keeping its status; /tasks unblock removes it
priority: P1
assignee: ai
created: 2026-10-13
depends-on: [TASK-588, TASK-589]
blocks: []
# findings: ids this task remediates — from a review/audit/harvest/drill pass, or from ordinary
# field use with no pass behind it at all. Prefixes: see /tasks intake
findings: []
pr: null
github-issue: null
jira-key: null
---

# Birko.Security.Grants.AspNetCore — RequireGrant, resolver decorator, DI, end-to-end README

## Context

Third of three tasks under [[STORY-062]]. It builds on [[TASK-588]] (grants) and [[TASK-589]]
(invitations, sign-in). The story owns the design (decisions 1, 7, 14–16).

The web integration is a **separate companion project**, decided 2026-10-13, so the core stays free of
ASP.NET: the same split as OAuth.Server ("pure handler library") and REST / REST.Server.

It plugs into what `Birko.Security.AspNetCore` already has: `IUserPermissionResolver`,
`PermissionResolutionMiddleware` (per-request and server-side, so a revoke is immediate), `ICurrentUser`,
`RequirePermission`, and `ClaimsPermissionChecker` with the `*` wildcard.

⚠ `PermissionResolutionMiddleware` parses the user-id claim as a `Guid`. A raw external `sub` (GitHub's
numeric id, Google's opaque string) doesn't parse and resolves **nothing**. The host must map
`(provider, subject)` to a local `User.Guid` via `UserLogin` and issue its **own** token with
`sub` = the local Guid. This is the first thing a consumer gets wrong, so the README leads with it, and
`RequireGrant` answers it with a distinct error code.

## Acceptance criteria

- [ ] New shared project `Birko.Security.Grants.AspNetCore` (`.shproj` + `.projitems`, `CLAUDE.md`,
      `README.md`) depends on Grants + Security.AspNetCore, with `Microsoft.AspNetCore.App` supplied by the
      consumer. It is registered in `.slnx`, `.code-workspace`, the aggregator `.csproj`, `CLAUDE-projects.md`
      (catalog + Dependency Flow), `README.md` and `docs/security.md`.
- [ ] A Minimal API endpoint filter, `RequireGrant(resourceType, action, routeParam: "id")`:
      - it validates the action at build time;
      - unauthenticated → 401;
      - authenticated with no local Guid → 401 `Grant.UnmappedSubject`;
      - an unparseable id → 404;
      - denied, following the visibility rule ([[STORY-062]] decision 2b):
        - a `Public` type → 403 `{error, required}`;
        - a `Granted` type the caller can read → 403;
        - a `Granted` type the caller cannot read → a bare 404, indistinguishable from a missing resource;
      - a missing route value is a configuration bug and throws;
      - endpoints without the filter are untouched.
- [ ] `GrantAwareUserPermissionResolver` decorates the host's `IUserPermissionResolver`, so bootstrap
      admins surface as `*` and type-wide grants as `{type}:{action}` through the existing middleware.
      Per-resource grants are not surfaced. `UserPermissionResolverAdminSource` wraps the **inner**
      resolver, and wiring it to the decorated one is refused, so it cannot recurse.
- [ ] DI: `AddBirkoGrants(o => …)` registers options, table and services, and `.WithGrantPermissions()`
      installs the decorator and **throws** when no `IUserPermissionResolver` is registered (no silent
      no-op).
- [ ] The README shows the end-to-end wiring:
      - external OIDC login (`AddOpenIdConnect` / `AddGitHub`, with a cookie as the temporary scheme);
      - the `UserLogin` lookup by `(Provider, ProviderKey = sub)`, never by email alone;
      - setting `EmailVerified` from the provider's `email_verified` claim, never from `UserLogin.IsVerified`;
      - `OnSignInAsync`;
      - the app's own JWT with the local Guid as `sub`;
      - anonymous reads, `RequirePermission` for global writes, `RequireGrant` for per-resource writes, and
        `GrantOwnerOnCreateAsync` in the create handler;
      - the last-owner compensation caveat.
- [ ] Tests (added to `tests/Birko.Security.Grants.Tests`, with `FrameworkReference Microsoft.AspNetCore.App`):
      - every filter outcome above;
      - the decorator's union;
      - the decorator's refusal when unregistered;
      - the recursion guard.
- [ ] `Birko.Framework/CLAUDE.md` Recent Updates entry for the shipped story.
- [ ] The visibility rule (403 if the caller can read, otherwise 404; 401 before any lookup) recorded as a
      new rule in `CLAUDE-conventions.md` plus its index line in `CLAUDE.md`, naming `OwnedCrudResults` and
      `RequireGrant` as its two current producers.

## Out of scope

- Admin UI for grants and invitations (a Birko.Web page): consumer work or a later task.
- Adopting it in Reps or the presentation project: consumer-side tasks.
- OIDC in `Birko.Security.OAuth.Server`: a separate decision, only if a central SSO is ever needed.

## Human test plan

- [ ] In a scratch consumer wired per the README, sign in with GitHub as the configured bootstrap admin.
      Expected: the admin can create, edit and delete any resource. An anonymous browser can read, and
      every write returns 401.
- [ ] Sign in as a second GitHub account with no grants. Expected: reads work, and writes return 403.
      Invite that account's (verified) email as `owner` of one resource and sign in again. Expected: it
      can edit that resource and gets 403 on a different one.
- [ ] Declare a second resource type as `Granted` (private). As the second account, with no grant, edit
      one of its resources by id. Expected: 404, the same as for an id that does not exist. Give that
      account `viewer` on it and retry the edit. Expected: 403.
- [ ] Revoke the grant while the second user is signed in. Expected: the very next write returns 403,
      without signing out (no stale token).

## Implementation plan

Design: [[STORY-062]] § Design, decisions 1, 7, 14–16. Steps:

1. **Scaffold** `Birko.Security.Grants.AspNetCore/` with `/new-birko-subproject`, and register it as in
   TASK-588's step 2. Add the Security.AspNetCore, Security.Jwt and Data.Tenant projitems to the test
   project, plus the `FrameworkReference`.
2. **Filter:** `Authorization/RequireGrantEndpointFilter.cs` and `GrantEndpointExtensions.RequireGrant`.
   Mirror `PermissionEndpointFilter`'s response shape.
3. **Admin source:** `UserPermissionResolverAdminSource`, with a constructor guard against receiving the
   decorator.
4. **Decorator:** `GrantAwareUserPermissionResolver`.
5. **DI:** `Extensions/GrantServiceExtensions.cs` with `AddBirkoGrants` and `WithGrantPermissions`. TryAdd
   `SystemDateTimeProvider`.
6. **Tests:** use the `DefaultHttpContext` pattern from `PermissionEndpointFilterTests`.
7. **README:** the wiring sample above; link it from the core README.
8. **Finish:** the Recent Updates entry and the per-project `CLAUDE.md`. Run the tests and
   `/verify-birko-conventions`. One commit. Then run the human test plan → `verify` / `done`.
