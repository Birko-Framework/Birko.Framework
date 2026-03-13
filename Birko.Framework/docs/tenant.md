# Multi-Tenancy Guide

## Overview

Birko.Data.Tenant provides multi-tenancy support with automatic tenant filtering and assignment. Tenant context propagates through stores and repositories to ensure complete data isolation.

## Core Components

### TenantContext

Static context holding the current tenant:

```csharp
TenantContext.Current = new TenantContext
{
    TenantId = tenantGuid,
    TenantName = "Acme Corp"
};

var current = TenantContext.Current;
```

### TenantEntity

Base entity with tenant association:

```csharp
public class TenantEntity : AbstractModel
{
    public Guid TenantId { get; set; }
}
```

### TenantFilter

Expression filter for tenant isolation.

## Tenant-Aware Stores

```csharp
// Available variants:
TenantStore<T>           // Sync
TenantBulkStore<T>       // Sync + bulk
AsyncTenantStore<T>      // Async
AsyncTenantBulkStore<T>  // Async + bulk
```

Stores automatically:
- **On Create**: Set `TenantId` from `TenantContext.Current`
- **On Read**: Filter results to current tenant only
- **On Update/Delete**: Verify entity belongs to current tenant

## Tenant-Aware Repositories

```csharp
TenantRepository<T>       // Sync
AsyncTenantRepository<T>  // Async
```

## Tenant Resolution

### ASP.NET Core Middleware

```csharp
app.UseMiddleware<TenantMiddleware>();
```

Resolution strategies:

| Strategy | Example |
|----------|---------|
| Subdomain | `tenant1.yourapp.com` |
| Header | `X-Tenant-ID: {guid}` |
| Query parameter | `?tenant=tenant-name` |
| Route parameter | `/tenants/{tenantId}/...` |

## Usage Example

### Define Tenant Entity

```csharp
public class Customer : TenantEntity
{
    public string Name { get; set; }
    public string Email { get; set; }
}
```

### Use Tenant Store

```csharp
// Set tenant context (typically done by middleware)
TenantContext.Current = new TenantContext { TenantId = tenantId };

// All operations are automatically scoped to current tenant
var store = new AsyncTenantBulkStore<Customer>(underlyingStore);

// Create auto-assigns TenantId
await store.CreateAsync(new Customer { Name = "John" });

// Read only returns current tenant's data
var customers = await store.ReadAsync();
```

### Database Schema

```sql
CREATE TABLE customers (
    id UUID PRIMARY KEY,
    tenant_id UUID NOT NULL,
    name TEXT,
    email TEXT,
    FOREIGN KEY (tenant_id) REFERENCES tenants(id)
);
CREATE INDEX idx_customers_tenant ON customers(tenant_id);
```

### PostgreSQL Row-Level Security (Optional)

For additional safety at the database level:

```sql
ALTER TABLE customers ENABLE ROW LEVEL SECURITY;

CREATE POLICY customer_tenant_policy ON customers
    USING (tenant_id = current_setting('app.tenant_id')::UUID);
```

## See Also

- [Birko.Data.Tenant CLAUDE.md](../Birko.Data.Tenant/CLAUDE.md)
- [Birko.Data.Sync.Tenant CLAUDE.md](../Birko.Data.Sync.Tenant/CLAUDE.md)
