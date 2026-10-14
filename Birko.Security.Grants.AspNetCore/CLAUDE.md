# Birko.Security.Grants.AspNetCore

## Overview
ASP.NET Core integration for `Birko.Security.Grants` (TASK-590, STORY-062). It is kept apart from the core so the
grant model stays usable from workers and gRPC hosts without ASP.NET, the same split as OAuth.Server and
REST / REST.Server.

## Project Location
`Birko.Security.Grants.AspNetCore/`

## Structure
```
Birko.Security.Grants.AspNetCore/
├── GrantsAspNetCoreOptions.cs                         — ReadAction (default "read"): the action meaning "may see it"
├── Authorization/
│   ├── RequireGrantEndpointFilter.cs                  — the filter + GrantEndpointExtensions.RequireGrant (an endpoint-filter FACTORY)
│   ├── GrantAwareUserPermissionResolver.cs            — decorates the host resolver: "*" for admins, "{type}:{action}" per type-wide grant
│   └── UserPermissionResolverAdminSource.cs           — IGrantAdminSource: admin grant OR the host resolver's "*"
└── Extensions/GrantServiceExtensions.cs               — AddBirkoGrants(...) → BirkoGrantsBuilder; .WithGrantPermissions()
```

## Dependencies
- `Birko.Security.Grants`, `Birko.Security.AspNetCore` (`ICurrentUser`, `IUserPermissionResolver`)
- `Microsoft.AspNetCore.App`: **consumer-supplied**, never declared in the projitems (NETSDK1087, TASK-234)

## Rules the code keeps
- **The visibility rule decides 404 vs 403** (STORY-062 decision 2b; rulebook rule 65). On a `Public` type a denial is 403.
  On a `Granted` type, one more `CanAsync(…, ReadAction)` runs, **only on the deny path**: 403 if it allows, otherwise
  a bare `Results.NotFound()` with no body, so the response can't be told apart from a missing resource.
- **401 comes before any lookup**, with `Grant.UnmappedSubject` when the caller is authenticated but has no `UserGuid`.
  That is the host issuing a token with the provider's raw subject.
- **Validation happens when the endpoints are built**, the first enumeration of the endpoint data source, which is
  normally the first request and not `app.Run()`. It never happens per request.
  - `RequireGrant` is an `AddEndpointFilterFactory`, so an undeclared type, action or read action throws
    `GrantConfigurationException` there.
  - An endpoint convention checks the route pattern too: a missing or optional `routeParam` throws
    `InvalidOperationException`. Otherwise it would be a 500 any caller could trigger.
  - A request that still lacks the value throws the same exception, because it is a configuration error, not a "no".
- **The tenant comes from `ICurrentUser.TenantGuid`**, the one source (rule 38).
- **`WithGrantPermissions` refuses rather than no-ops** (rules 51, 55). It throws when no non-keyed host
  `IUserPermissionResolver` is registered, when called twice, and when the host registered its **own**
  `IGrantAdminSource`, which it would otherwise replace silently. `AddBirkoGrants` marks its default source so the two
  cases can be told apart.
  - It re-registers the host's descriptor (instance, factory or type) under an internal holder type, so the
    decorator and the admin source both reach the **undecorated** resolver.
  - The decorator **keeps the host resolver's lifetime**. The grant store must therefore live at least as long, and
    scope validation reports a mismatch.
  - The holder **disposes a resolver it built**, from a type or a factory, because the container no longer sees it as
    the registered service. An instance the host passed in is left to the host.
- **A retired role or type is skipped by the decorator, never thrown.** The decorator runs in middleware on every
  request, so a throw would turn every endpoint into a 500 for that user. `IGrantService` still refuses wherever the
  type or role is actually checked.
- **The tenant `Guid.Empty` means no tenant**, as `PermissionResolutionMiddleware` reads it.
- **`:` is refused in role, action and type names** (core table). Type-wide grants surface as `{type}:{action}`, and
  a `:` inside a name would let two different grants produce the same permission string.
- Per-resource grants are **never** surfaced as permission strings, because there are unboundedly many.

## Maintenance
See `Birko.Framework/CLAUDE-maintenance.md`. Tests live in `tests/Birko.Security.Grants.Tests/AspNetCore/`.
