---
id: STORY-062
parent: EPIC-020
# status — one of: planned, in-progress, done, cancelled
status: in-progress
created: 2026-10-13
---

# Per-resource access grants and invitations (Birko.Security.Grants)

## User story

As a consumer developer, I want to mark some users as global admins and others as owners or editors of
specific resources, and to invite people by email before they have signed in, so that my app can keep
reads public and gate writes per resource without building its own grant model.

## Behaviour

- A global admin can do everything. An owner of resource X can edit, delete and share X, and gets 403
  on resource Y. An anonymous caller can read and gets 401 on any write.
- A revoke takes effect on the next request, not at the next sign-in.
- An invitation redeems only when the provider says the email is **verified**.
- The first admin comes from configuration as `provider:subject`, never "first user to sign up".
- A resource never loses its last owner.

## Tasks

Split from one task at planning (2026-10-13). Each is one commit:

| Task | Slice | Depends on |
|---|---|---|
| [[TASK-588]] | Core project: role table, `ResourceGrant`, store, `CanAsync`, List, Grant / Revoke, owner-on-create | — |
| [[TASK-589]] | Invitations + bootstrap admins + `OnSignInAsync` | TASK-588 |
| [[TASK-590]] | `Birko.Security.Grants.AspNetCore`: `RequireGrant`, resolver decorator, DI, README wiring, Recent Updates | TASK-588, TASK-589 |
| [[TASK-593]] | Grant predicates verified on a SQL backend (SQLite) — spawned at TASK-588's close | TASK-588 |

## Related work elsewhere in the tree

- [[TASK-541]] (EPIC-019): an owner-or-admin check for entities that carry **one owner column**. That
  complements grants rather than duplicating them: it is the simple case, and grants are the
  many-owners / invited / role case.
  **Settled 2026-10-13:** its 404 and `RequireGrant`'s codes are one rule, the visibility rule
  (decision 2b). Owned data is private, so it is the "cannot read → 404" row.
- [[TASK-521]] (EPIC-019): GitHub web sign-in helpers (state store, code exchange, user-info). TASK-590's
  README should use them once they exist. ⚠ GitHub's `/user` email is not a verified signal; the verified
  flag comes from `/user/emails`. Whatever feeds `SignInContext.EmailVerified` from GitHub must read that.
- [[TASK-517]] (EPIC-019): `OAuthServer` issuing a token pair for a subject that signed in elsewhere. It is
  an alternative to `Birko.Security.Jwt` for the "issue our own token with the local Guid" step.

## Design (shared by the three tasks)

_Drafted 2026-10-13 by `/tasks plan` (Plan agent, grounded in the code). Decisions settled by the user the
same day: **two projects**, **the email-verified signal is the provider's `email_verified` claim**, and **this
three-task split**. Not grilled yet._

1. **Layers.**
   - `Birko.Security.Grants` depends on Data.Core, Data.Stores and Time.Abstractions. It has no ASP.NET,
     no Models.Users and no DI package, and is composed through constructors like `OAuthServer`.
   - `Birko.Security.Grants.AspNetCore` depends on Grants and Security.AspNetCore. The consumer supplies
     `Microsoft.AspNetCore.App`.
   - Add a Security block to `CLAUDE-projects.md` § Dependency Flow; there is none today.
   - Core doesn't import Models.Users. Sign-in receives provider, subject, email and `emailVerified`, and
     the `UserLogin` lookup lives in the host, shown in the README.
2. **Role table.** `GrantOptions.DefineRole(name, params actions)` builds an immutable `GrantRoleTable`.
   - It is validated once, when built. These throw `GrantConfigurationException`: an empty or duplicate
     role, an empty action, an `OwnerRole` (default `owner`) that is undeclared or lacks the `ShareAction`
     (default `share`), and a role named `*` (reserved).
   - At runtime, an undeclared action or role also throws `GrantConfigurationException`. It never quietly
     returns false (rules 41, 55).
2b. **Resource-type visibility, which decides 404 vs 403.** Settled with the user 2026-10-13.
   - **The rule:** *if the caller may read the resource, a denied action is **403**; if they may not read
     it, every denial is **404**.* That way a response never reveals that a private resource exists, and
     never claims that a resource the caller can see is "not found".
   - **The declaration:** each resource type declares its visibility with
     `GrantOptions.ResourceType(name, Visibility.Public | Visibility.Granted)`.
   - **`Granted` is the default**, so a forgotten declaration fails closed: it hides existence, never leaks it.
   - **Refusals:** an undeclared resource type, passed to `CanAsync`, `RequireGrant` or Grant, refuses with
     `GrantConfigurationException`, like an undeclared role (decision 2). `Visibility.Public` affects only
     the denial code. It does **not** make `CanAsync(…, "read")` true; public reads are anonymous endpoints
     that never ask.
   - **The same rule covers existing code.** `OwnedCrudResults`' 404-for-foreign and [[TASK-541]] are this
     rule's private-data row.
   - **Unauthenticated is still 401**, decided before any lookup, so it reveals nothing about a resource.
   - **Rulebook:** record it as a rule in CLAUDE-conventions.md when TASK-590 lands, so the next endpoint
     doesn't pick a code by taste.
3. **Models** (all `AbstractModel`).
   - `ResourceGrant`: UserGuid, ResourceType, ResourceId?, Role, TenantGuid?, GrantedBy?, CreatedAt.
     It does **not** implement `ITenant`: that interface has a non-null tenant, and null here means
     "untenanted", so tenant scoping is explicit (decision 5).
   - `Invitation`: Email (trimmed, lower-invariant), ResourceType, ResourceId?, Role, TenantGuid?,
     TokenHash, InvitedBy, CreatedAt, ExpiresAt, RedeemedAt?, RedeemedBy?, RevokedAt?.
4. **Stores.**
   - `IResourceGrantStore` and `IInvitationStore` are both `IAsyncBulkStore<T>`, because the lookups
     return collections.
   - The named lookups are default interface methods, following the `IRefreshTokenStore` pattern:
     `ListForUserAsync`, `ListForResourceAsync`, `CountRoleAsync`, `GetByTokenHashAsync` and
     `ListPendingForEmailAsync`.
   - Lookups use the 5-arg `ReadAsync` or `ReadFirstAsync`, never the bulk `Read(filter)`, which returns
     a collection rather than one entity.
   - Tests use `AsyncInMemoryStore<T>` subclasses, as `tests/Birko.Security.OAuth.Server.Tests/InMemoryStore.cs`
     does.
5. **Tenant scoping (rules 38 and 39).**
   - Every call takes one explicit `Guid? tenantGuid`. Core never reads an ambient tenant; the filter
     supplies `ICurrentUser.TenantGuid`.
   - The predicate is `g.TenantGuid == null || g.TenantGuid == tenant`. With no tenant in scope it is
     only `g.TenantGuid == null`, so null means "no tenant scope", never "don't filter" (rule 5).
   - Writes stamp the tenant of the scope onto the grant.
6. **`CanAsync(user, type, id, action, tenant?)`.**
   - Validate the action against the table.
   - Allow if `IGrantAdminSource.IsAdminAsync(user, tenant)` is true.
   - Otherwise load the user's grants for that type in scope, keep those with
     `ResourceId == null || ResourceId == id`, and allow if any of their roles includes the action.
   - `id == null` (e.g. "create") matches type-wide grants only.
   - Nothing is cached, so a revoke takes effect on the next request.
7. **Admin source.**
   - The core default, `BootstrapAdminGrantSource`, checks for the reserved grant
     `{ResourceType="*", ResourceId=null, Role="*"}`. The table refuses `*` as a role, so no consumer role
     can collide with it.
   - The companion's `UserPermissionResolverAdminSource` adds `"*"` from the **inner, undecorated**
     `IUserPermissionResolver`. Given the decorated one, it would recurse; a constructor check and a test
     guard this.
8. **Grant / Revoke authorization.**
   - The caller must be an admin, or hold `share` on the resource.
   - **Escalation:** a non-admin may only grant a role whose actions are a subset of the caller's own
     effective actions on that resource (type-wide ∪ exact). Comparing action sets means the table needs
     no role ranking.
   - Revoke checks the same against the **target's** role, so an editor holding `share` can't remove an
     owner.
   - Re-granting the same (user, type, id, tenant) replaces the role rather than adding a duplicate.
   - Refusals throw `GrantRefusedException(Code)`, in the `OAuthServerException` style. Codes:
     `Grant.NotAuthorized`, `Grant.Escalation`, `Grant.LastOwner`, and
     `Invitation.Expired/Used/Revoked/EmailNotVerified/EmailMismatch`.
9. **Last owner.**
   - Revoking or demoting an owner of an **exact** resource counts that resource's owners first and refuses
     at 1. Type-wide owners don't count.
   - Mutations on one key are serialized by an in-process keyed `SemaphoreSlim`, whose entry is removed on
     the last release (rule 36).
   - Multi-instance hosts get a check after the write: recount after the delete, and at 0 re-create the
     grant (as a fresh model; the InMemory store now refuses a duplicate create) and throw `Grant.LastOwner`.
   - This is a documented compensation, not a transaction. The README says so.
10. **`GrantOwnerOnCreateAsync`** skips the share check (the creator has no grant yet), refuses `id == null`,
    and is idempotent.
    - **It only claims a resource nobody owns yet.** If anyone else owns it, the call refuses
      `Grant.NotAuthorized`. Otherwise any caller who reached a create or upsert path with an existing id would
      become its owner. Amended at TASK-588's close review, 2026-10-14.
    - **Revoke and re-grant act on every row with the exact key**, not on the one entity read. A backend with no
      unique index can hold two rows for one key, and a revoke that deleted only one would report success while
      access remained (rule 22).
    - **`ListResourcesAsync` takes an action**, so a type-wide grant whose role lacks "read" never lists every
      resource.
11. **Invitations.**
    - Creating one uses the same authorization as Grant.
    - The token is 32 bytes from `RandomNumberGenerator`, base64url-encoded, and only its SHA-256 hex is
      stored. `Internal/TokenHasher` is new, because OAuth.Server's hasher is internal.
    - The plaintext token is returned once, in `InvitationCreated { Invitation, Token }`. Sending it is the
      host's job.
    - The lifetime comes from options (default 7 days), and time comes from `IDateTimeProvider`.
12. **Sign-in.** `OnSignInAsync(SignInContext { UserGuid, Provider, Subject, Email?, EmailVerified, TenantGuid?, InvitationToken? })`.
    The host fills `EmailVerified` from the provider's `email_verified` claim. **`UserLogin.IsVerified` is
    never the signal**: it is documented "always true for OAuth" (`Birko.Models.Users/Models/UserLogin.cs:50`).
    - **Bootstrap.** If `provider:subject` is in `GrantSignInOptions.BootstrapAdmins` (provider part
      case-insensitive), ensure the reserved admin grant exists, so a repeat sign-in is a no-op. Neither
      email nor "first user" is ever consulted.
    - **Invitations.** Runs only when `EmailVerified`. It redeems every pending invitation for the
      normalized email, plus the token's invitation if one was passed; that invitation must match the email.
    - **Each redemption** takes a keyed lock, re-reads the invitation, refuses an expired, used or revoked
      one, and re-checks that the inviter can still share. Then it grants with `GrantedBy = InvitedBy` and
      marks the invitation redeemed.
    - **Unverified email.** Nothing is redeemed and the result reports it in `SignInResult.Skipped`. An
      explicit token in that case throws `Invitation.EmailNotVerified`; it is never silently ignored
      (rule 51).
    - **As built in TASK-589 (2026-10-14), amended by its close review:**
      - `SignInContext` has **no `TenantGuid`**. Each invitation carries its own tenant, so a context tenant would
        be a parameter nothing reads (rule 51).
      - The options are `GrantSignInOptions`, not `GrantOptions`.
      - **The order is bootstrap, then email invitations, then the token, last.** A dead link therefore can't stop
        the others.
      - **Single use across processes** is a filtered-update claim plus a re-read. A failed grant releases the claim.
      - Redemption uses `GrantAtLeastAsync`, so its never-downgrade decision is made under the resource lock.
      - A retired role or type is reported as `Invitation.Invalid`, never thrown.
      - Emails are ASCII-only and fold A–Z alone (KELVIN SIGN would otherwise fold to `k`).
13. **Listing.**
    - `ListResourcesAsync` returns `GrantedResources { AllOfType, Ids }`, so a type-wide grant isn't lost.
    - `ListGranteesAsync` returns exact and type-wide grants, each flagged `IsTypeWide`.
14. **Resolver decorator.** `GrantAwareUserPermissionResolver` wraps the host's resolver.
    - It returns the inner permissions, plus `"*"` for bootstrap admins, plus `"{type}:{action}"` per
      type-wide grant.
    - Roles get `"{type}:{role}"` added.
    - Per-resource grants are **not** surfaced, because there are unboundedly many; that is `RequireGrant`'s
      job.
15. **`RequireGrant(type, action, routeParam = "id")`.** `routeParam: null` means a type-wide check.
    - The action is validated when the filter is built, which happens when the endpoints are built (normally the
      first request, not `app.Run()`). So is the route pattern: a missing or optional `routeParam` throws there.
    - Unauthenticated → **401**. Authenticated but with `UserGuid == null` (a raw external sub) → **401**
      with code `Grant.UnmappedSubject`, which points at the README.
    - A missing route value is a configuration bug and throws `InvalidOperationException`.
    - A value that doesn't parse as a Guid → **404**. Such a resource can't exist, and the 404 reveals
      nothing.
    - **A denied check follows decision 2b:**
      - On a `Public` type → **403** `{error, required}`, matching `PermissionEndpointFilter`.
      - On a `Granted` type, one more `CanAsync(…, read)` decides: **403** if the caller can read, otherwise
        a bare **404**, indistinguishable from a missing resource.
      - The extra lookup runs only on the deny path.
16. **DI.**
    - `AddBirkoGrants(o => …)` registers the options, the table and the services, and TryAdds
      `SystemDateTimeProvider`.
    - The host registers the stores; a missing one fails on first resolve.
    - `.WithGrantPermissions()` swaps in the resolver decorator and throws if no `IUserPermissionResolver`
      is registered.

### Tradeoffs (chosen first)

- **Companion split over one project.** Core works from workers and gRPC hosts too. Cost: one more
  project to register.
- **Escalation by action subset over role ranking.** No ordering config, and custom roles compare
  correctly. Cost: two roles with disjoint actions can't grant each other.
- **Bootstrap as a reserved persisted grant over a Models.Users `Role "*"`.** No dependency on Users.
  Cost: removing someone from config doesn't revoke them; `RevokeAdminAsync` (TASK-589) does. `RevokeAsync` cannot: type `*` is never declared.
- **Auto-redeem by verified email, with the token optional.** An invited person redeems just by signing
  in, and the token keeps the link flow available.
- **The visibility rule over always-403 or always-404.** Always-403 reveals that private resources exist
  and contradicts OwnedCrud. Always-404 answers "not found" on a resource the user can see. Cost: one
  declaration per resource type, and an extra lookup on the deny path for `Granted` types.
- **Last owner by lock plus post-write recount.** Works on every backend. Cost: not atomic, and documented.

### Risks

- `ResourceId == null` and nullable-tenant equality in expressions may translate differently per backend
  (rule 5). Only InMemory is tested here, so a SQL-backend run is a follow-up to spawn.
- Every `RequireGrant` call, and the decorator, reads the store, with no cache by design so that a revoke
  is immediate.
- The decorator can recurse if wired wrong; a constructor guard and a test cover it.
- Email normalization is trim plus lower-invariant; Unicode edge cases are not handled.
