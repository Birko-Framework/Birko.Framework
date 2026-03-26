# Birko.Data.Tenant

Multi-tenancy support for the Birko Framework providing tenant-aware data access and isolation.

## Features

- Thread-safe tenant context via AsyncLocal (supports async/await and nested scopes)
- Store wrappers that transparently filter reads and assign tenant on create
- Authorization checks — throws UnauthorizedAccessException on cross-tenant update/delete
- ASP.NET Core middleware for tenant resolution (header, query string, route, custom delegate)
- DI extensions for registering tenant-aware repositories
- Filter composition combining base filters with tenant predicates

## Installation

Shared project — import in your `.csproj`:

```xml
<Import Project="..\Birko.Data.Tenant\Birko.Data.Tenant.projitems" Label="Shared" />
```

## Dependencies

- Birko.Data.Core (AbstractModel)
- Birko.Data.Stores (IStore, IAsyncStore, IBulkStore, IAsyncBulkStore)
- Birko.Data.Filters (IFilter)
- Microsoft.AspNetCore.Http (middleware)
- Microsoft.Extensions.DependencyInjection

## Usage

### Tenant-Aware Entities

Implement `ITenant` on your model:

```csharp
using Birko.Data.Tenant.Models;

public class Customer : AbstractLogModel, ITenant
{
    public Guid TenantGuid { get; set; }
    public string? TenantName { get; set; }
    public string Name { get; set; }
}
```

### Setting Tenant Context

```csharp
using Birko.Data.Tenant.Models;

// Via static singleton
Tenant.Set(tenantGuid, "Acme Corp");
var currentId = Tenant.Id;
var isSet = Tenant.IsSet;
Tenant.Clear();

// Scoped execution (restores previous tenant on completion)
Tenant.Current.WithTenant(tenantGuid, "Acme Corp", () =>
{
    // All store operations here are scoped to this tenant
});

// Async scoped
await Tenant.Current.WithTenantAsync(tenantGuid, "Acme Corp", async () =>
{
    await store.CreateAsync(customer);
});
```

### Wrapping Stores

```csharp
using Birko.Data.Tenant.Stores;

// Wrap any existing store with tenant awareness
IAsyncStore<Customer> innerStore = new MyCustomerStore();
IAsyncStore<Customer> tenantStore = innerStore.AsTenantAware();

// All operations now automatically filter/assign by current tenant
await tenantStore.CreateAsync(customer);    // TenantGuid set automatically
var all = await tenantStore.ReadAsync();    // Filtered by current tenant
```

### ASP.NET Core Integration

```csharp
// Register tenant context in DI
builder.Services.AddTenantContextScoped();

// Add middleware to pipeline
app.UseTenantMiddleware(options =>
{
    options.TenantHeaderName = "X-Tenant-Id";       // default
    options.TenantNameHeaderName = "X-Tenant-Name";  // default
    options.RequireTenant = true;                     // return 401 if no tenant
    options.TenantQueryStringKey = "tenantId";        // optional
    options.TenantRouteKey = "tenantId";              // optional
    options.CustomTenantResolver = ctx => /* ... */;  // optional custom logic
});
```

### DI Repository Registration

```csharp
using Birko.Data.Tenant.Repositories;

// Register a tenant-aware async repository
services.AddTenantAsyncRepository<CustomerStore, CustomerRepository, Customer>();

// With custom store factory
services.AddTenantAsyncRepository<CustomerRepository, Customer>(
    sp => new CustomerStore(sp.GetRequiredService<ISettings>()));
```

## API Reference

### Models

| Type | Description |
|------|-------------|
| **ITenant** | Interface: `TenantGuid`, `TenantName` |
| **ITenantContext** | Context: `CurrentTenantGuid`, `HasTenant`, `SetTenant()`, `ClearTenant()`, `WithTenant()` |
| **TenantContext** | Default ITenantContext using AsyncLocal |
| **Tenant** | Static singleton: `Current`, `Id`, `Name`, `IsSet`, `Set()`, `Clear()` |

### Store Wrappers

| Wrapper | Wraps | Description |
|---------|-------|-------------|
| **TenantStoreWrapper** | IStore | Sync single-item tenant wrapper |
| **TenantBulkStoreWrapper** | IBulkStore | Sync bulk tenant wrapper |
| **AsyncTenantStoreWrapper** | IAsyncStore | Async single-item tenant wrapper |
| **AsyncTenantBulkStoreWrapper** | IAsyncBulkStore | Async bulk tenant wrapper |

### Filters

| Filter | Description |
|--------|-------------|
| **ModelByTenant\<T\>** | Combines base filter with tenant GUID predicate |

### Middleware

| Component | Description |
|-----------|-------------|
| **TenantMiddleware** | Per-request tenant resolution |
| **TenantMiddlewareOptions** | Header names, query/route keys, RequireTenant, custom resolvers |
| **UseTenantMiddleware()** | IApplicationBuilder extension |
| **AddTenantContext()** | IServiceCollection extensions (Scoped/Singleton/Transient) |

## Tenant Resolution Order

The middleware resolves tenant ID in this order (first match wins):
1. HTTP header (`X-Tenant-Id` by default)
2. Query string (if `TenantQueryStringKey` configured)
3. Route parameter (if `TenantRouteKey` configured)
4. Custom resolver (if `CustomTenantResolver` delegate provided)

## Related Projects

- [Birko.Data.Core](../Birko.Data.Core/) - Models and core types
- [Birko.Data.Stores](../Birko.Data.Stores/) - Store interfaces
- [Birko.Data.Sync.Tenant](../Birko.Data.Sync.Tenant/) - Tenant-aware synchronization
- [Birko.Security.AspNetCore](../Birko.Security.AspNetCore/) - ASP.NET Core security with tenant resolution

## Filter-Based Bulk Operations

Tenant wrappers automatically compose the tenant filter with user-provided filters for all filter-based operations:
- `Update(filter, PropertyUpdate<T>)` / `Update(filter, Action<T>)` — filter is scoped to current tenant
- `Delete(filter)` — filter is scoped to current tenant

## License

Part of the Birko Framework.
