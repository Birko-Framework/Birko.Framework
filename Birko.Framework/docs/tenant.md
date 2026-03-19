# Multi-Tenancy Guide

## Overview

Birko.Data.Tenant provides multi-tenancy support with automatic tenant filtering, assignment, and authorization enforcement. Tenant context uses `AsyncLocal` for thread-safe propagation through async operations.

## Core Components

### ITenant

Implement this interface on any model that should be tenant-aware:

```csharp
using Birko.Data.Tenant.Models;

public class Customer : AbstractLogModel, ITenant
{
    public Guid TenantGuid { get; set; }
    public string? TenantName { get; set; }
    public string Name { get; set; }
}
```

### Tenant Context

Thread-safe context using `AsyncLocal<T>`:

```csharp
using Birko.Data.Tenant.Models;

// Static singleton accessor
Tenant.Set(tenantGuid, "Acme Corp");
var id = Tenant.Id;             // Guid?
var name = Tenant.Name;         // string?
var isSet = Tenant.IsSet;       // bool
Tenant.Clear();

// Scoped execution (saves/restores previous tenant)
Tenant.Current.WithTenant(tenantGuid, "Acme Corp", () =>
{
    // Operations here scoped to this tenant
    // Previous tenant restored when scope exits
});

// Async scoped
await Tenant.Current.WithTenantAsync(tenantGuid, "Acme Corp", async () =>
{
    await store.CreateAsync(customer);
});

// With return value
var result = await Tenant.Current.WithTenantAsync(tenantGuid, null, async () =>
{
    return await store.ReadAsync();
});
```

### ITenantContext

Full interface for DI-based usage:

```csharp
public interface ITenantContext
{
    Guid? CurrentTenantGuid { get; }
    string? CurrentTenantName { get; }
    bool HasTenant { get; }
    void SetTenant(Guid tenantGuid, string? tenantName = null);
    void ClearTenant();
    TResult? WithTenant<TResult>(Guid tenantGuid, string? tenantName, Func<TResult> action);
    Task<TResult?> WithTenantAsync<TResult>(Guid tenantGuid, string? tenantName, Func<Task<TResult>> action);
    void WithTenant(Guid tenantGuid, string? tenantName, Action action);
    Task WithTenantAsync(Guid tenantGuid, string? tenantName, Func<Task> action);
}
```

## Store Wrappers

Wrap any existing store to add transparent tenant filtering:

```csharp
using Birko.Data.Tenant.Stores;

// Extension method (auto-detects bulk variant)
IAsyncStore<Customer> tenantStore = innerStore.AsTenantAware();

// Or explicitly with custom tenant context
IAsyncStore<Customer> tenantStore = innerStore.AsTenantAware(myTenantContext);
```

Available wrappers:

| Wrapper | Wraps | Description |
|---------|-------|-------------|
| `TenantStoreWrapper<TStore, T>` | `IStore<T>` | Sync single-item |
| `TenantBulkStoreWrapper<TStore, T>` | `IBulkStore<T>` | Sync bulk |
| `AsyncTenantStoreWrapper<TStore, T>` | `IAsyncStore<T>` | Async single-item |
| `AsyncTenantBulkStoreWrapper<TStore, T>` | `IAsyncBulkStore<T>` | Async bulk |

All wrappers implement `IStoreWrapper<T>` to access the inner store.

### Behavior

- **Create**: Automatically sets `TenantGuid` and `TenantName` from current context
- **Read**: Automatically filters results to current tenant via `ModelByTenant<T>` filter
- **Update/Delete**: Throws `UnauthorizedAccessException` if entity doesn't belong to current tenant
- **No tenant set**: Operations pass through without filtering (non-tenant mode)

## Tenant Filter

`ModelByTenant<TModel>` combines an optional base filter with tenant GUID predicate:

```csharp
using Birko.Data.Tenant.Filters;

// Filter by tenant + custom condition
var filter = new ModelByTenant<Customer>(tenantGuid, x => x.Name.StartsWith("A"));
Expression<Func<Customer, bool>>? expr = filter.Filter();
// Result: x => x.Name.StartsWith("A") && x.TenantGuid == tenantGuid
```

## ASP.NET Core Integration

### Register Services

```csharp
using Birko.Data.Tenant.Middleware;

// Register ITenantContext in DI
builder.Services.AddTenantContextScoped();    // For web apps (per-request)
// builder.Services.AddTenantContextSingleton(); // For non-web apps
```

### Add Middleware

```csharp
app.UseTenantMiddleware(options =>
{
    options.TenantHeaderName = "X-Tenant-Id";        // default
    options.TenantNameHeaderName = "X-Tenant-Name";   // default
    options.RequireTenant = true;                      // 401 if no tenant found
    options.TenantQueryStringKey = "tenantId";         // optional
    options.TenantRouteKey = "tenantId";               // optional
    options.CustomTenantResolver = ctx =>              // optional custom logic
    {
        // Return Guid? from HttpContext
        return null;
    };
});
```

### Tenant Resolution Order

The middleware resolves tenant ID in this order (first match wins):
1. HTTP header (`X-Tenant-Id` by default)
2. Query string (if `TenantQueryStringKey` configured)
3. Route parameter (if `TenantRouteKey` configured)
4. Custom resolver (if `CustomTenantResolver` delegate provided)

### Register Tenant-Aware Repositories

```csharp
using Birko.Data.Tenant.Repositories;

// Auto-wraps store with tenant filtering
services.AddTenantAsyncRepository<CustomerStore, CustomerRepository, Customer>();

// With custom store factory
services.AddTenantAsyncRepository<CustomerRepository, Customer>(
    sp => new CustomerStore(sp.GetRequiredService<ISettings>()));

// Convenience scoped variants
services.AddTenantAsyncRepositoryScoped<CustomerStore, CustomerRepository, Customer>();
```

## Database Schema

### Shared Database, Shared Schema

```sql
CREATE TABLE customers (
    id UUID PRIMARY KEY,
    tenant_guid UUID NOT NULL,
    tenant_name TEXT,
    name TEXT,
    email TEXT
);
CREATE INDEX idx_customers_tenant ON customers(tenant_guid);
```

### PostgreSQL Row-Level Security (Optional)

For additional safety at the database level:

```sql
ALTER TABLE customers ENABLE ROW LEVEL SECURITY;

CREATE POLICY customer_tenant_policy ON customers
    USING (tenant_guid = current_setting('app.tenant_guid')::UUID);
```

## See Also

- [Birko.Data.Tenant](https://github.com/birko/Birko.Data.Tenant)
- [Birko.Data.Sync.Tenant](https://github.com/birko/Birko.Data.Sync.Tenant)
- [Security Guide](security.md) — ASP.NET Core tenant resolution via Birko.Security.AspNetCore
