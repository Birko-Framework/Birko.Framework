---
id: TASK-588
parent: STORY-062
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: done
# blocked: <reason> — add this line while the task is blocked, keeping its status; /tasks unblock removes it
priority: P1
assignee: ai
created: 2026-10-13
depends-on: []
blocks: [TASK-589, TASK-590]
# findings: ids this task remediates — from a review/audit/harvest/drill pass, or from ordinary
# field use with no pass behind it at all. Prefixes: see /tasks intake
findings: []
pr: null
github-issue: null
jira-key: null
---

# Birko.Security.Grants core — role table, per-resource grants, CanAsync, Grant/Revoke

## Context

First of three tasks under [[STORY-062]]. The story owns the full design (decisions 1–16, tradeoffs,
risks), so read it first. Everything this task builds is pure .NET with no ASP.NET dependency.

Why it exists: consumers (Reps, the presentation project) need public reads and gated writes behind an
external login. Global authority already exists: `Birko.Models.Users` `Role` / `UserRole` /
`RolePermission`, plus `Birko.Security.AspNetCore` `IUserPermissionResolver` / `RequirePermission` / the
`*` wildcard. **Per-resource** authority ("owns presentation `3c9e…`") does not. A permission string per
resource (`presentation:3c9e…:edit`) is unbounded and can't answer "everything user X owns", so a grant
model is needed.

Reuse, don't duplicate: `Birko.Data.Stores` (`IAsyncBulkStore<T>`), `Birko.Data.Core` (`AbstractModel`),
`Birko.Time.Abstractions` (`IDateTimeProvider`). `Birko.Security.OAuth.Server` is the structural example
for a store-backed security project (stores as interfaces with named lookups, refusal exceptions with
codes).

## Acceptance criteria

- [x] New shared project `Birko.Security.Grants` (`.shproj` + `.projitems`, `CLAUDE.md`, `README.md`),
      scaffolded via `/new-birko-subproject`. It is registered in `Birko.Framework.slnx`, `.code-workspace`
      and the aggregator `.csproj`, and in `CLAUDE-projects.md` (catalog + a new Security block in
      § Dependency Flow). Dependencies: Data.Core, Data.Stores, Time.Abstractions only (story decision 1).
- [x] `ResourceGrant` model (`AbstractModel`): `UserGuid`, `ResourceType`, `ResourceId?` (**null = all
      resources of that type**), `Role`, `TenantGuid?`, `GrantedBy?`, `CreatedAt`. Persisted through
      `IResourceGrantStore : IAsyncBulkStore<ResourceGrant>` (any backend).
- [x] Roles map to actions through a declared, consumer-configurable `GrantRoleTable`, for example
      `owner` → `read, edit, delete, share`. An undeclared role or action **refuses** with
      `GrantConfigurationException`, never silently grants or denies. `*` is reserved.
- [x] Each resource type is declared with its visibility,
      `GrantOptions.ResourceType(name, Visibility.Public | Visibility.Granted)`, defaulting to `Granted`.
      An undeclared resource type refuses with `GrantConfigurationException` (story decision 2b).
      Visibility does not change what `CanAsync` returns; it is read by TASK-590's filter to pick 403 or 404.
- [x] `IGrantService.CanAsync(user, resourceType, resourceId, action, tenant?)` is true when a type-wide
      grant or a grant on that exact id allows the action, or when the `IGrantAdminSource` says admin
      (story decisions 6–7). It is never cached. Also `ListResourcesAsync` and `ListGranteesAsync`
      (decision 13).
- [x] `GrantAsync` / `RevokeAsync`: only an admin or a caller who can `share` may grant. Nobody grants a
      role whose actions exceed their own on that resource, and revoke checks the target's role the same
      way. Revoking or demoting the **last owner** refuses, including under two concurrent revokes
      (decisions 8–9). Re-grant replaces, never duplicates.
- [x] `GrantOwnerOnCreateAsync(user, resourceType, resourceId, tenant?)` makes the creator the owner. It
      is idempotent and refuses a null id (decision 10).
- [x] Tenant: a grant with `TenantGuid` applies only in that tenant, and a check with a tenant in scope
      never matches another tenant's grant. Untenanted grants match everywhere (decision 5, rules 38/39).
- [x] Tests in new `tests/Birko.Security.Grants.Tests` (xUnit + FluentAssertions, InMemory stores):
      - the table's refusals;
      - global vs per-resource allow, and deny on another id;
      - reserved admin grant;
      - escalation when granting and when revoking;
      - last owner, including concurrent revokes;
      - owner-on-create;
      - tenant isolation;
      - listing, including a type-wide grant.

## Out of scope

- Invitations, bootstrap admins, sign-in: [[TASK-589]].
- `RequireGrant`, resolver decorator, DI, README wiring, Recent Updates: [[TASK-590]].
- Running the scoped predicates against a SQL backend → deferred to [[TASK-593]] (the story's first risk).

## Human test plan

N/A — fully covered by automated tests. This task has no host and no HTTP surface. The story's
end-to-end manual steps are on [[TASK-590]].

## Implementation plan

Design: [[STORY-062]] § Design, decisions 1–10, 13. Steps:

1. **Scaffold** `Birko.Security.Grants/` with `/new-birko-subproject`, plus `tests/Birko.Security.Grants.Tests/`
   (net10.0, xUnit, FluentAssertions). The test project imports the projitems of Time.Abstractions,
   Data.Core, Data.Stores, Data.InMemory and Grants. Follow `tests/Birko.Security.OAuth.Server.Tests`.
2. **Register:**
   - `Birko.Framework.slnx`, under `/Security/` and `/Tests/`;
   - `.code-workspace`;
   - the aggregator `Consumers/Birko.Sandbox/Birko.Framework/Birko.Framework.csproj`, next to OAuth.Server;
   - `CLAUDE-projects.md`, in the catalog and a new Security block in Dependency Flow;
   - `README.md` and `docs/security.md`;
   - the folder-group list in `CLAUDE-maintenance.md`.
3. **Configuration, test-first:** `GrantOptions` (`DefineRole`, `ResourceType` + `Visibility`, `OwnerRole`,
   `ShareAction`), `GrantRoleTable`,
   `GrantConfigurationException`, `GrantRefusedException` and `GrantErrorCodes`. Write the table's refusal
   tests first.
4. **Model and store:** `Models/ResourceGrant.cs` and `Stores/IResourceGrantStore.cs`. Put the named lookups
   in default interface methods, with the scoped predicates as private static expressions in one place.
5. **Admin source:** `IGrantAdminSource` and `BootstrapAdminGrantSource`, which detects the reserved
   `*` grant (TASK-589 writes it).
6. **Service:** `IGrantService` / `GrantService` with Can, ListResources, ListGrantees, Grant, Revoke and
   GrantOwnerOnCreate. Add `Internal/KeyedLock.cs`, and the post-write recount compensation for the last
   owner.
7. **Tests:** one class per area, using `TestDateTimeProvider`. For concurrent revokes, use `Task.WhenAll`.
8. **Finish:** the project's `CLAUDE.md` and `README.md` (core API only; wiring is TASK-590). Run
   `dotnet test tests/Birko.Security.Grants.Tests`, then `/verify-birko-conventions`. One commit, staged
   explicitly.

Critical files: `Birko.Security.Grants/Services/GrantService.cs`, `Birko.Security.Grants/Stores/IResourceGrantStore.cs`,
`Birko.Security.Grants/GrantRoleTable.cs`, `Birko.Framework/CLAUDE-projects.md`, `Birko.Framework/Birko.Framework.slnx`.
