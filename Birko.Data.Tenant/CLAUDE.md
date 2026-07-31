# Birko.Data.Tenant

## Overview
Multi-tenancy support for the Birko data layer. Provides tenant context management, automatic tenant filtering via store wrappers, tenant-aware filters, ASP.NET Core middleware for tenant resolution, and DI extensions.

## Project Location
`C:\Source\Birko.Data.Tenant\`

## Components

### Models (`Birko.Data.Tenant.Models`)
- **ITenant** — Interface for tenant-aware entities: `Guid TenantGuid`, `string? TenantName`
- **ITenantContext** — Interface for current tenant management: `CurrentTenantGuid`, `CurrentTenantName`, `HasTenant`, `SetTenant()`, `ClearTenant()`, `WithTenant()` / `WithTenantAsync()` (scoped execution)
- **TenantContext** — Default ITenantContext implementation using `AsyncLocal<T>` for thread-safe storage. Supports nested scopes (saves/restores previous tenant)
- **Tenant** — Static singleton accessor: `Tenant.Current` (ITenantContext), `Tenant.Id`, `Tenant.Name`, `Tenant.IsSet`, `Tenant.Set()`, `Tenant.Clear()`

### Filters (`Birko.Data.Tenant.Filters`)
- **ModelByTenant\<TModel\>** — `IFilter<TModel>` where `TModel : AbstractModel, ITenant`. Combines optional base filter with tenant GUID check via `Expression.AndAlso`. **Contract: only `null` means "no tenant in scope"** and yields the base filter alone; `Guid.Empty` is a tenant *value* and is filtered on like any other id. It used to short-circuit alongside `null`, so a `Guid.Empty` scope read **every** tenant's rows while the store wrapper still refused writes against `Guid.Empty` — reads failed open, writes failed closed. Deliberately does not throw on `Guid.Empty`: the value can come off an `X-Tenant-Id` header, so throwing would be a client-triggerable 500; `EnsureTenantForStrict` is what throws for the genuinely-unset `null` case

### Stores (`Birko.Data.Tenant.Stores`)
- **TenantStoreWrapper\<TStore, T\>** — Sync `IStore<T>` wrapper. Auto-filters reads by tenant, auto-assigns tenant on create, throws `TenantMismatchException` on cross-tenant update/delete (when a tenant is set — see **Authorization** below for the no-tenant fail-open mode)
- **TenantScopeRequiredException** — `InvalidOperationException` subclass carrying `Operation` (`read`/`create`) and `EntityType`, thrown by `EnsureTenantForStrict` / `SetTenantGuidIfNeeded` under `TenantIsolationMode.Strict` when no tenant is in scope. Lets a host answer **400 "send a tenant"** instead of the blanket **500** a bare `InvalidOperationException` gets — measured on a consumer (Symbio TASK-271): 115 of 170 parameterless GET routes returned 500 to a header-less call, purely because the type carried no signal. Subclassing keeps every existing `catch (InvalidOperationException)` working. **Distinct from `TenantMismatchException` on purpose**: "no tenant in scope" is a request-shaped problem (400), "the row belongs to another tenant" is authorization-shaped (403) — conflating them sends a caller missing a header hunting for a permission
- **TenantMismatchException** — `UnauthorizedAccessException` subclass carrying `Operation`, `EntityType`, `ExpectedTenantGuid`, `ActualTenantGuid`. Lets a host report "this row is another tenant's" distinctly from "you lack a permission" — the two used to be indistinguishable, so hosts reported a tenant-scope refusal as a generic 403 authorization failure. Subclassing keeps every existing `catch (UnauthorizedAccessException)` working. **Do not echo the tenant ids to callers** — they are for the server log
- **TenantBulkStoreWrapper\<TStore, T\>** — Extends TenantStoreWrapper, implements `IBulkStore<T>` with bulk CRUD + ordering/paging
- **AsyncTenantStoreWrapper\<TStore, T\>** — Async `IAsyncStore<T>` wrapper (same semantics as sync)
- **AsyncTenantBulkStoreWrapper\<TStore, T\>** — Extends AsyncTenantStoreWrapper, implements `IAsyncBulkStore<T>`
- **TenantStoreExtensions** — `AsTenantAware<T>()` extension methods for IStore and IAsyncStore (auto-detects bulk variant)

All wrappers implement `IStoreWrapper<T>` for accessing the inner store.

### Repositories (`Birko.Data.Tenant.Repositories`)
- **RepositoryServiceCollectionExtensions** — DI extension methods:
  - `AddTenantRepository<TStore, TRepository, TModel>()` — Registers sync store with tenant wrapper
  - `AddTenantAsyncRepository<TStore, TRepository, TModel>()` — Registers async store with tenant wrapper
  - Overloads with `Func<IServiceProvider, IStore<TModel>>` factory
  - Convenience `*Scoped` variants

### Middleware (`Birko.Data.Tenant.Middleware`)
- **TenantMiddleware** — ASP.NET Core middleware for automatic tenant resolution per-request
- **TenantMiddlewareOptions** — Configuration:
  - `TenantHeaderName` (default: `"X-Tenant-Id"`)
  - `TenantNameHeaderName` (default: `"X-Tenant-Name"`)
  - `TenantQueryStringKey` — Optional query string resolution
  - `TenantRouteKey` — Optional route parameter resolution
  - `RequireTenant` — Return 401 if no tenant found (default: false)
  - `CustomTenantResolver` — `Func<HttpContext, Guid?>` delegate
  - `CustomTenantNameResolver` — `Func<HttpContext, Guid, string?>` delegate
- **TenantMiddlewareExtensions** — `UseTenantMiddleware()` extension for IApplicationBuilder
- **ServiceCollectionExtensions** — `AddTenantContext()`, `AddTenantContextSingleton()`, `AddTenantContextScoped()`, `AddTenantContextTransient()`

## File Structure
```
Models/
├── ITenant.cs
├── ITenantContext.cs
└── TenantContext.cs
Filters/
└── ModelByTenant.cs
Stores/
├── TenantStoreExtensions.cs
├── TenantStoreWrapper.cs
├── TenantBulkStoreWrapper.cs
├── AsyncTenantStoreWrapper.cs
└── AsyncTenantBulkStoreWrapper.cs
Repositories/
└── RepositoryServiceCollectionExtensions.cs
Middleware/
├── TenantMiddleware.cs
└── ServiceCollectionExtensions.cs
```

## Dependencies
- **Birko.Data.Core** — AbstractModel
- **Birko.Data.Stores** — IStore, IAsyncStore, IBulkStore, IAsyncBulkStore, IStoreWrapper, StoreDataDelegate, OrderBy
- **Birko.Data.Filters** — IFilter, ModelByGuid
- **Birko.Serialization** — ISerializer for error response serialization in TenantMiddleware (optional, defaults to SystemJsonSerializer)
- **Microsoft.AspNetCore.Http** — HttpContext, RequestDelegate (for middleware)
- **Microsoft.Extensions.DependencyInjection** — ServiceCollection extensions

## Key Patterns
- **Wrapper/Decorator:** Store wrappers transparently add tenant filtering to any store
- **AsyncLocal storage:** Thread-safe, async-aware tenant context without thread-local
- **Nested scopes:** WithTenant/WithTenantAsync save/restore previous context
- **All-tenants (admin) scope:** `WithAllTenants(...)` sets `IsAllTenantsScope`. Inside it, reads/counts
  span **all** tenants even when a tenant is also set — `TenantFilter` treats the effective tenant as
  null while the scope is active (so admin/maintenance code that reads global reference data
  `TenantGuid == Guid.Empty` or other tenants' rows from within a request scope actually sees them, not
  just the ambient tenant). It also suppresses the Strict no-tenant throw. Note it only changes the
  filter seam — `CurrentTenantGuid` is unchanged, so a nested `WithTenant(...)` used purely for event
  attribution still stamps that tenant.
- **Filter composition:** ModelByTenant combines base filters with tenant predicate via Expression.AndAlso
- **Typed refusals:** every tenancy refusal has its own exception type, each subclassing the one hosts already
  catch, so a host can map it to the right status instead of a generic 500/403 — `TenantScopeRequiredException`
  (no tenant in scope → 400) and `TenantMismatchException` (wrong tenant → 403). A new tenancy condition gets a
  new type; never make hosts match on message text
- **Authorization:** Update/Delete throw `TenantMismatchException` (an `UnauthorizedAccessException`) for cross-tenant access —
  **only when a tenant is set.** With no tenant on the context (`HasTenant == false`) the wrappers
  deliberately FAIL OPEN ("non-tenant/admin mode": reads unfiltered, writes allowed across tenants;
  CR-L229, test-pinned). Fail-closed callers must set a tenant or override the virtual
  `BelongsToCurrentTenant`
- **A tenant assertion the caller controls is not a tenant check (SH-H047).** Item-level `Update`/`Delete`
  read the targeted row back — by `Guid`, deliberately **unscoped by tenant** — and authorize against
  *that*, never against `item.TenantGuid`. `ITenant.TenantGuid` is a public settable property, normally
  model-bound from a request body, and the inner stores key writes on the primary field alone, so
  comparing it to the ambient tenant let `{ Guid = <another tenant's row>, TenantGuid = mine }` overwrite
  or delete that row. The read must stay unscoped: a tenant-scoped one returns null for a foreign row,
  which is indistinguishable from "no such row" — and "no such row" authorizes the write. This is the
  store-layer sibling of the `X-Tenant-Id`/JWT-claim guard in `Birko.Security.AspNetCore`.
  - `BelongsToCurrentTenant` stays the override seam but is now **handed the persisted row**; an override
    that consults anything caller-controlled re-opens the hole.
  - `Update` also restores the stored `TenantGuid`/`TenantName` onto the item (`PreserveStoredTenant`), so
    an owner cannot **re-home** a row by editing the field. Skipped with no tenant set and inside
    `WithAllTenants(...)`, matching `SetTenantGuidIfNeeded`'s behaviour on create in those scopes.
  - Cost: one read per item-level write. The bulk wrappers override `ReadStoredItems` to resolve a whole
    batch in a single `ModelsByGuid` read, and authorize the entire batch before stamping any of it.
  - The **filter-based** bulk paths (`Update(filter, …)`, `Delete(filter)`) were never affected — they
    already compose `TenantFilter`

## Related Projects
- [Birko.Data.Sync.Tenant](../Birko.Data.Sync.Tenant/CLAUDE.md) — Tenant-aware synchronization
- [Birko.Security.AspNetCore](../Birko.Security.AspNetCore/CLAUDE.md) — ASP.NET Core tenant resolution (header/subdomain strategies)

## Maintenance

### README Updates
When making changes that affect the public API, features, or usage patterns of this project, update the README.md accordingly.

### CLAUDE.md Updates
When making major changes to this project, update this CLAUDE.md to reflect new or renamed files, changed architecture, dependencies, or conventions.

### Test Requirements
Every new public functionality must have corresponding unit tests.
