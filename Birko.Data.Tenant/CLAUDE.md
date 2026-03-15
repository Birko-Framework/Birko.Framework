# Birko.Data.Tenant

## Overview
Multi-tenancy support for the Birko data layer providing tenant-aware data access.

## Project Location
`C:\Source\Birko.Data.Tenant\`

## Purpose
- Multi-tenant data isolation
- Tenant context management
- Automatic tenant filtering
- Tenant-specific migrations

## Components

### Stores
- `TenantStore<T>` - Tenant-aware store
- `TenantBulkStore<T>` - Tenant-aware bulk store
- `AsyncTenantStore<T>` - Async tenant-aware store
- `AsyncTenantBulkStore<T>` - Async tenant-aware bulk store

### Repositories
- `TenantRepository<T>` - Tenant-aware repository
- `AsyncTenantRepository<T>` - Async tenant-aware repository

### Models
- `Tenant` - Tenant entity
- `TenantEntity` - Base entity with tenant ID

### Filters
- `TenantFilter` - Filter by tenant

### Middleware
- `TenantMiddleware` - ASP.NET Core middleware for tenant resolution

## Tenant Context

```csharp
using Birko.Data.Tenant;

// Set current tenant
TenantContext.Current = new TenantContext
{
    TenantId = tenantId,
    TenantName = "Acme Corp"
};

// Get current tenant
var currentTenant = TenantContext.Current;
```

## Implementation

```csharp
using Birko.Data.Tenant.Stores;

public class CustomerStore : TenantStore<Customer>
{
    public override IEnumerable<Customer> ReadAll()
    {
        // Automatically filtered by current tenant
        var items = base.ReadAll();
        return items.Where(x => x.TenantId == TenantContext.Current.TenantId);
    }

    public override Guid Create(Customer item)
    {
        // Automatically set tenant ID
        item.TenantId = TenantContext.Current.TenantId;
        return base.Create(item);
    }
}
```

## Tenant Entity

```csharp
public class TenantEntity : Entity
{
    public Guid TenantId { get; set; }
}
```

## Middleware

```csharp
// ASP.NET Core integration
app.UseMiddleware<TenantMiddleware>();
```

Tenant resolution from:
- Subdomain (tenant1.app.com)
- Header (X-Tenant-ID)
- Query parameter
- Cookie
- Route parameter

## Dependencies
- Birko.Data.Core, Birko.Data.Stores, Birko.Data.Repositories
- Microsoft.AspNetCore.Http (for middleware)

## Tenant Strategies

### Subdomain
```
tenant1.yourapp.com → TenantId from subdomain
```

### Header
```
X-Tenant-ID: 123e4567-e89b-12d3-a456-426614174000
```

### Query Parameter
```
https://yourapp.com?tenant=tenant-name
```

### Route Parameter
```
https://yourapp.com/tenants/{tenantId}/...
```

## Features

### Automatic Filtering
Queries automatically filter by tenant:
```csharp
var customers = store.ReadAll(); // Only returns current tenant's customers
```

### Automatic Assignment
New entities automatically get tenant ID:
```csharp
var customer = new Customer { Name = "John" };
store.Create(customer); // TenantId automatically set
```

### Tenant Isolation
Each tenant's data is completely isolated:
- Separate queries
- Separate security contexts
- Separate caches

## Database Design

### Shared Database, Shared Schema
```sql
CREATE TABLE customers (
    id UUID PRIMARY KEY,
    tenant_id UUID NOT NULL,
    name TEXT,
    FOREIGN KEY (tenant_id) REFERENCES tenants(id)
);

CREATE INDEX idx_customers_tenant ON customers(tenant_id);
```

### Row-Level Security (PostgreSQL)
```sql
ALTER TABLE customers ENABLE ROW LEVEL SECURITY;

CREATE POLICY customer_tenant_policy ON customers
    USING (tenant_id = current_setting('app.tenant_id')::UUID);
```

## Best Practices

1. **Always index tenant_id** - For query performance
2. **Use row-level security** - For additional safety (PostgreSQL)
3. **Tenant context** - Always validate tenant context
4. **Cross-tenant queries** - Explicitly disable filters if needed
5. **Tenant caching** - Cache tenant information

## Security

```csharp
// Validate tenant access
if (!await tenantService.CanAccess(TenantContext.Current.TenantId, requestedTenantId))
{
    throw new UnauthorizedAccessException();
}
```

## Use Cases
- SaaS applications
- Multi-organization systems
- White-label applications
- Department-based data separation

## Related Projects
- [Birko.Data.Sync.Tenant](../Birko.Data.Sync.Tenant/CLAUDE.md) - Tenant-aware synchronization

## Maintenance

### README Updates
When making changes that affect the public API, features, or usage patterns of this project, update the README.md accordingly. This includes:
- New classes, interfaces, or methods
- Changed dependencies
- New or modified usage examples
- Breaking changes

### CLAUDE.md Updates
When making major changes to this project, update this CLAUDE.md to reflect:
- New or renamed files and components
- Changed architecture or patterns
- New dependencies or removed dependencies
- Updated interfaces or abstract class signatures
- New conventions or important notes

### Test Requirements
Every new public functionality must have corresponding unit tests. When adding new features:
- Create test classes in the corresponding test project
- Follow existing test patterns (xUnit + FluentAssertions)
- Test both success and failure cases
- Include edge cases and boundary conditions
