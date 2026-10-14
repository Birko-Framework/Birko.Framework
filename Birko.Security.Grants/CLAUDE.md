# Birko.Security.Grants

## Overview
Per-resource access grants: who is owner, editor or viewer of **one** resource, or of every resource of a type.
Complements the global authorization in `Birko.Security` / `Birko.Security.AspNetCore` (roles, permission strings,
`RequirePermission`, the `*` wildcard), which cannot say "owns presentation `3c9e…`". Pure library — no ASP.NET
dependency; persistence flows through `Birko.Data.Stores`, so it runs on any backend.

Design: `Birko.Framework/tasks/EPIC-020-security-access-control/STORY-062-per-resource-access-grants/STORY.md`.
Core grants: TASK-588. Invitations, bootstrap admins and the sign-in hook: TASK-589. `RequireGrant`, the
permission-resolver decorator and DI: TASK-590, a separate `Birko.Security.Grants.AspNetCore` companion.

## Project Location
`Birko.Security.Grants/`

## Structure
```
Birko.Security.Grants/
├── GrantOptions.cs                 — DefineRole / ResourceType / OwnerRole / ShareAction → Build()
├── GrantRoleTable.cs               — validated, immutable role → action table + resource types; Wildcard "*" reserved
├── ResourceVisibility.cs           — Granted (default) | Public — read by the HTTP layer for 404 vs 403
├── GrantErrorCodes.cs              — Grant.NotAuthorized / Grant.Escalation / Grant.LastOwner
├── GrantConfigurationException.cs  — undeclared or invalid configuration (never a "no")
├── GrantRefusedException.cs        — a write refused by a rule; carries Code
├── Models/ResourceGrant.cs         — UserGuid, ResourceType, ResourceId? (null = type-wide), Role, TenantGuid?, GrantedBy?, CreatedAt
├── Stores/
│   ├── IResourceGrantStore.cs      — IAsyncBulkStore<ResourceGrant> + named lookups as default methods
│   └── GrantPredicates.cs          — the one producer of every grant query (tenant scope, null handling)
├── Services/
│   ├── IGrantService.cs / GrantService.cs — CanAsync, ListResources, ListGrantees, Grant, Revoke, GrantOwnerOnCreate
│   ├── IGrantAdminSource.cs        — "admin everywhere?" seam
│   ├── BootstrapAdminGrantSource.cs — admin = holder of the reserved grant {"*", null, "*"}
│   ├── GrantedResources.cs         — GrantedResources (AllOfType + Ids), GrantEntry (Grant + IsTypeWide)
│   ├── IInvitationService.cs / InvitationService.cs — Create (token returned once), Revoke; internal Redeem
│   ├── IGrantSignInService.cs / GrantSignInService.cs — OnSignInAsync(SignInContext): bootstrap, then invitations
│   └── IAdminGrantService.cs / AdminGrantService.cs — RevokeAdminAsync; internal EnsureBootstrapAdmin
├── GrantSignInOptions.cs           — BootstrapAdmins ("provider:subject"), InvitationLifetime (default 7 days)
├── Models/Invitation.cs            — Email (normalized), resource, role, tenant, TokenHash, InvitedBy, expiry, redeemed / revoked
├── Stores/IInvitationStore.cs      — GetByTokenHash, ListPendingForEmail
└── Internal/
    ├── KeyedLock.cs                — per-key async mutex; entries removed on last release
    └── TokenHasher.cs              — 32 random bytes base64url; SHA-256 hex is what is stored
```

## Dependencies
- `Birko.Data.Core` — `AbstractModel`
- `Birko.Data.Stores` — `IAsyncBulkStore<T>`
- `Birko.Time.Abstractions` — `IDateTimeProvider`

No NuGet packages, no ASP.NET, no `Birko.Models.Users`: the host maps its login to a user Guid.

## Rules the code keeps
- **Undeclared names refuse.** Every lookup of a role, action or resource type missing from the table throws
  `GrantConfigurationException` — including a stored grant whose role was later removed from the table. Never
  answer "no" for a typo (rules 41, 55). `"*"` is reserved and refused as any consumer name.
- **Tenant is explicit, from one source** (rules 38/39). Every call takes `Guid? tenantGuid`; the service never
  reads an ambient tenant. In scope: untenanted grants plus the tenant's; with no tenant, untenanted only.
- **Null is never compared through a variable** (rule 5). `GrantPredicates` branches on null and renders an
  explicit `== null`, because `col == @p` with a null parameter matches nothing in SQL. Add new queries **there**.
- **Grant / revoke authorization:** admin, or `share` plus every action of the role granted and the role replaced
  (no escalation; compared as action sets, so no role ranking). Anyone may revoke their **own** grant. The admin
  check runs **before** the replaced role is resolved, so an admin can still remove a grant whose role was retired.
- **Writes by key, not by entity.** Revoke deletes, and re-grant updates, every row matching `GrantPredicates.Exact`.
  A backend without a unique index can hold two rows for one key, and touching only the one read would report
  success while access remained (rule 22).
- **Owner-on-create only claims an unowned resource.** If someone else already owns it, the call refuses
  `Grant.NotAuthorized`. This is what keeps an upsert path from making a caller owner of an existing resource.
- **Listing is per action.** `ListResourcesAsync` keeps only grants whose role allows the action passed.
- **The reserved admin grant** (`*`) cannot be written or revoked through `IGrantService`, because `*` is never a
  declared type. TASK-589 owns writing it (bootstrap) and removing it.
- **Last owner:** exact-resource owners only (type-wide owners do not count); serialised in-process by `KeyedLock`
  (shared process-wide instance), plus a post-write recount that re-creates / restores the grant and throws when
  another process won the race. A compensation, not a transaction:
  - The recount and the restore use `CancellationToken.None` once the write has happened.
  - A re-created grant has a **new** Guid.
  - The count is per exact tenant, so an untenanted owner does not count towards tenant A. That is stricter than
    necessary, and never permissive.
- **No caching**, so a revoke is effective on the next check.

### Invitations and sign-in (TASK-589)
- **The verified signal is the provider's, passed in.** `SignInContext.EmailVerified` comes from OIDC's
  `email_verified` claim, or from GitHub's `/user/emails` `verified` flag. It is **never** `UserLogin.IsVerified`,
  which is "always true for OAuth". An unverified email redeems nothing. It is reported in `Skipped`, and an explicit
  token with an unverified email throws.
- **Redemption goes through `IGrantService.GrantAtLeastAsync` as the inviter.** That re-authorizes the inviter now,
  so one who has since lost `share` invites nobody. It also only ever adds actions: a user who already holds them
  keeps their role, a lesser role is upgraded, and an incomparable role is `Grant.RoleConflict`. The decision is
  made under the resource lock, so a concurrent promotion is never overwritten.
- **Single use across processes is a claim.** A filtered `PropertyUpdate` sets `RedeemedAt` / `RedeemedBy` only
  where the invitation is still unredeemed and unrevoked, and a re-read shows whose claim stuck. That gives one winner
  only where the backend runs it as **one conditional statement**: SQL (`UPDATE … WHERE`) and MongoDB (per-document
  `$set`). `AbstractAsyncBulkStore`'s portable fallback reads and then writes, so file and in-memory stores get single
  use within one process only, from the lock. SQL is measured by TASK-593. A claim whose grant then fails is released. Revoke claims the same way,
  so a revoke and a redemption can't both succeed. The per-invitation `KeyedLock` covers a single process.
- **A stale invitation never breaks sign-in.** If its role or type has been retired, it is reported as
  `Invitation.Invalid` and nothing is thrown. Redemptions found by email report failures. Only the explicit token
  throws, and it is processed **last**, so a dead link costs nothing but its own redemption.
- **Emails are ASCII, folded A–Z only.** `ToLowerInvariant` would fold KELVIN SIGN U+212A to `k`, letting a
  different, verified mailbox match someone else's invitation. `CreateAsync` refuses non-ASCII addresses and any
  address without exactly one `@`.
- **Bootstrap admins are `provider:subject`**, with the provider compared case-insensitively and the subject
  exactly. A malformed entry throws at construction. There is no email path and no "first user" path. The admin
  grant written is untenanted and idempotent.
- **`RevokeAdminAsync`** may be called by an admin, or by anyone removing themselves. It refuses to remove the last
  admin of a tenant scope (`Grant.LastAdmin`), with the same lock and post-write recount as the last owner. A
  tenant's admins include the global (untenanted) ones, because those administer every tenant, while a global admin
  counts only among the global ones. It counts grants, so admins coming from an `IGrantAdminSource` are invisible
  to it. That only ever makes the rule stricter.

## Maintenance
See `Birko.Framework/CLAUDE-maintenance.md`. Tests: `tests/Birko.Security.Grants.Tests/`.
