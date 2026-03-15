# Birko.Data.Tenant

Multi-tenancy support for the Birko Framework providing tenant-aware data access and isolation.

## Features

- Automatic tenant filtering on all queries
- Automatic tenant ID assignment on create
- Tenant resolution via subdomain, header, query parameter, cookie, or route
- ASP.NET Core middleware integration
- Row-level security support (PostgreSQL)

## Installation

```bash
dotnet add package Birko.Data.Tenant
```

## Dependencies

- Birko.Data.Core (AbstractModel)
- Birko.Data.Stores (store interfaces, Settings)
- Microsoft.AspNetCore.Http (middleware)

## Usage

```csharp
using Birko.Data.Tenant;

// Set tenant context
TenantContext.Current = new TenantContext { TenantId = tenantId, TenantName = "Acme Corp" };

// Use tenant-aware store
var store = new TenantStore<Customer>();
store.Create(customer);   // TenantId automatically set
var all = store.ReadAll(); // Filtered by current tenant

// ASP.NET Core middleware
app.UseMiddleware<TenantMiddleware>();
```

## API Reference

### Stores

- **TenantStore\<T\>** / **TenantBulkStore\<T\>** - Sync tenant stores
- **AsyncTenantStore\<T\>** / **AsyncTenantBulkStore\<T\>** - Async tenant stores

### Models

- **Tenant** - Tenant entity
- **TenantEntity** - Base entity with TenantId

### Middleware

- **TenantMiddleware** - Resolves tenant from request (subdomain, header, query, cookie, route)

## Related Projects

- [Birko.Data.Core](../Birko.Data.Core/) - Models and core types
- [Birko.Data.Stores](../Birko.Data.Stores/) - Store interfaces
- [Birko.Data.Sync.Tenant](../Birko.Data.Sync.Tenant/) - Tenant-aware synchronization

## License

Part of the Birko Framework.
