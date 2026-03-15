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
- **ModelByTenant\<TModel\>** — `IFilter<TModel>` where `TModel : AbstractModel, ITenant`. Combines optional base filter with tenant GUID check via `Expression.AndAlso`

### Stores (`Birko.Data.Tenant.Stores`)
- **TenantStoreWrapper\<TStore, T\>** — Sync `IStore<T>` wrapper. Auto-filters reads by tenant, auto-assigns tenant on create, throws `UnauthorizedAccessException` on cross-tenant update/delete
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
- **Microsoft.AspNetCore.Http** — HttpContext, RequestDelegate (for middleware)
- **Microsoft.Extensions.DependencyInjection** — ServiceCollection extensions

## Key Patterns
- **Wrapper/Decorator:** Store wrappers transparently add tenant filtering to any store
- **AsyncLocal storage:** Thread-safe, async-aware tenant context without thread-local
- **Nested scopes:** WithTenant/WithTenantAsync save/restore previous context
- **Filter composition:** ModelByTenant combines base filters with tenant predicate via Expression.AndAlso
- **Authorization:** Update/Delete throw UnauthorizedAccessException for cross-tenant access

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
