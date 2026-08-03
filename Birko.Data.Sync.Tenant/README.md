# Birko.Data.Sync.Tenant

Tenant-aware synchronization for multi-tenant applications in the Birko Framework.

## Features

- TenantSyncProvider for tenant-scoped sync operations
- TenantSyncQueue for managing per-tenant sync queues
- Tenant context integration

## Tenant scoping

Every run is scoped to exactly one tenant, taken from `TenantSyncOptions.TenantGuid` or — when that is
null — from the ambient `ITenantContext`. That one value scopes what is **fetched** from both stores, so
another tenant's rows are never compared, previewed, version-hashed, recorded in the knowledge store or
deleted.

`PreviewAsync` / `SyncAsync` refuse rather than guess:

| Situation | Result |
|---|---|
| Only `options.TenantGuid` set | Scoped to it — the background-job shape |
| Only an ambient tenant set | Scoped to it |
| Both set and equal | Scoped to it |
| Both set and **different** | `TenantMismatchException` |
| Neither set, entity has `TenantGuid` | `TenantScopeRequiredException` |
| Neither set, entity has no `TenantGuid` | Runs unscoped — nothing to scope by |
| Inside `WithAllTenantsAsync(...)` | Cross-tenant on purpose; an explicit `TenantGuid` still narrows it |

```csharp
// Sync one tenant from a background job — no ambient context needed.
await provider.SyncAsync(new TenantSyncOptions { Scope = "invoices", TenantGuid = tenantGuid });

// Sync every tenant on purpose.
await tenantContext.WithAllTenantsAsync(() => provider.SyncAsync(new TenantSyncOptions { Scope = "invoices" }));
```

## Installation

```bash
dotnet add package Birko.Data.Sync.Tenant
```

## Dependencies

- Birko.Data.Core (AbstractModel)
- Birko.Data.Stores (store interfaces, Settings)
- Birko.Data.Sync
- Birko.Data.Tenant

## Related Projects

- [Birko.Data.Sync](../Birko.Data.Sync/) - Core sync framework
- [Birko.Data.Tenant](../Birko.Data.Tenant/) - Multi-tenancy support

## License

Part of the Birko Framework.
