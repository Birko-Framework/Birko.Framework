using System;
using System.Threading.Tasks;
using Birko.Data.Tenant.Models;

namespace Birko.Framework.Examples.Data
{
    /// <summary>
    /// Examples demonstrating the Birko.Data.Tenant multi-tenancy framework.
    /// TenantContext uses AsyncLocal for thread-safe tenant storage.
    /// TenantStoreWrapper auto-filters store operations by current tenant.
    /// </summary>
    public static class TenantExamples
    {
        /// <summary>
        /// Basic tenant context: setting and reading the current tenant.
        /// </summary>
        public static Task RunBasicTenantContextExample()
        {
            ExampleOutput.WriteLine("=== Basic Tenant Context Example ===\n");

            // ITenant interface: TenantGuid (Guid), TenantName (string?)
            // TenantContext implements ITenantContext using AsyncLocal
            // Tenant static class provides application-wide singleton access

            var tenantGuid = Guid.NewGuid();

            // Set the current tenant using the static Tenant helper
            ExampleOutput.WriteLine($"Setting tenant: {tenantGuid}");
            Tenant.Set(tenantGuid, "Acme Corp");

            ExampleOutput.WriteLine($"Current tenant ID: {Tenant.Id}");
            ExampleOutput.WriteLine($"Current tenant name: {Tenant.Name}");
            ExampleOutput.WriteLine($"Is tenant set: {Tenant.IsSet}");

            // Clear the tenant (switch to non-tenant mode)
            Tenant.Clear();
            ExampleOutput.WriteLine($"\nAfter clearing:");
            ExampleOutput.WriteLine($"Is tenant set: {Tenant.IsSet}");
            ExampleOutput.WriteLine($"Current tenant ID: {Tenant.Id?.ToString() ?? "(null)"}");

            // Use ITenantContext directly for dependency injection
            ITenantContext context = Tenant.Current;
            context.SetTenant(Guid.NewGuid(), "Beta Corp");
            ExampleOutput.WriteLine($"\nUsing ITenantContext directly:");
            ExampleOutput.WriteLine($"Has tenant: {context.HasTenant}");
            ExampleOutput.WriteLine($"Tenant ID: {context.CurrentTenantGuid}");
            ExampleOutput.WriteLine($"Tenant name: {context.CurrentTenantName}");

            context.ClearTenant();
            ExampleOutput.WriteLine("\n=== Example Complete ===");
            return Task.CompletedTask;
        }

        /// <summary>
        /// Scoped tenant operations using WithTenant.
        /// </summary>
        public static Task RunScopedTenantExample()
        {
            ExampleOutput.WriteLine("=== Scoped Tenant Example ===\n");

            var tenantAId = Guid.NewGuid();
            var tenantBId = Guid.NewGuid();

            ITenantContext context = new TenantContext();

            // WithTenant executes an action within a tenant scope,
            // then automatically restores the previous tenant.
            ExampleOutput.WriteLine("Executing scoped operations:");

            context.WithTenant(tenantAId, "Tenant A", () =>
            {
                ExampleOutput.WriteLine($"  Inside Tenant A scope: {context.CurrentTenantName} ({context.CurrentTenantGuid})");

                // Nested scope: temporarily switch to Tenant B
                context.WithTenant(tenantBId, "Tenant B", () =>
                {
                    ExampleOutput.WriteLine($"  Inside nested Tenant B scope: {context.CurrentTenantName} ({context.CurrentTenantGuid})");
                });

                // Back to Tenant A after nested scope exits
                ExampleOutput.WriteLine($"  Back in Tenant A scope: {context.CurrentTenantName}");
            });

            ExampleOutput.WriteLine($"After scoped block: HasTenant = {context.HasTenant}");

            // WithTenant with return value
            var result = context.WithTenant(tenantAId, "Tenant A", () =>
            {
                return $"Data fetched for {context.CurrentTenantName}";
            });
            ExampleOutput.WriteLine($"\nScoped return value: {result}");

            ExampleOutput.WriteLine("\n=== Example Complete ===");
            return Task.CompletedTask;
        }

        /// <summary>
        /// TenantStoreWrapper: auto-filtering store operations by tenant.
        /// </summary>
        public static Task RunTenantStoreWrapperExample()
        {
            ExampleOutput.WriteLine("=== Tenant Store Wrapper Example ===\n");

            // TenantStoreWrapper<TStore, T> wraps any IStore<T> to auto-filter by tenant.
            // The model T must implement both AbstractModel and ITenant.
            //
            // Usage:
            // var innerStore = new MyJsonStore<TenantDocument>();
            // var tenantStore = new TenantStoreWrapper<MyJsonStore<TenantDocument>, TenantDocument>(
            //     innerStore, tenantContext);
            //
            // When a tenant is set in the context:
            //   - Create: automatically sets TenantGuid and TenantName on the item
            //   - Read: filters results to only return items belonging to current tenant
            //   - Update: throws UnauthorizedAccessException if item doesn't belong to tenant
            //   - Delete: throws UnauthorizedAccessException if item doesn't belong to tenant
            //   - Count: only counts items belonging to current tenant

            ExampleOutput.WriteLine("TenantStoreWrapper<TStore, T> behavior:");
            ExampleOutput.WriteLine("  Create(item) -> sets item.TenantGuid from context, then delegates to inner store");
            ExampleOutput.WriteLine("  Read(filter)  -> combines filter with tenant filter via ModelByTenant<T>");
            ExampleOutput.WriteLine("  Update(item) -> checks BelongsToCurrentTenant, throws if not");
            ExampleOutput.WriteLine("  Delete(item) -> checks BelongsToCurrentTenant, throws if not");
            ExampleOutput.WriteLine("  Count(filter) -> adds tenant filter to count query");
            ExampleOutput.WriteLine("  Save(item)   -> Create or Update based on Guid presence");

            ExampleOutput.WriteLine("\nSecurity:");
            ExampleOutput.WriteLine("  - Cross-tenant access throws UnauthorizedAccessException");
            ExampleOutput.WriteLine("  - When no tenant is set (non-tenant mode), all items are accessible");

            // Also available:
            // - TenantBulkStoreWrapper<TStore, T> for IBulkStore<T>
            // - AsyncTenantStoreWrapper<TStore, T> for IAsyncStore<T>
            // - AsyncTenantBulkStoreWrapper<TStore, T> for IAsyncBulkStore<T>

            ExampleOutput.WriteLine("\nAvailable wrappers:");
            ExampleOutput.WriteLine("  TenantStoreWrapper<TStore, T>          - wraps IStore<T>");
            ExampleOutput.WriteLine("  TenantBulkStoreWrapper<TStore, T>      - wraps IBulkStore<T>");
            ExampleOutput.WriteLine("  AsyncTenantStoreWrapper<TStore, T>     - wraps IAsyncStore<T>");
            ExampleOutput.WriteLine("  AsyncTenantBulkStoreWrapper<TStore, T> - wraps IAsyncBulkStore<T>");

            ExampleOutput.WriteLine("\n=== Example Complete ===");
            return Task.CompletedTask;
        }

        /// <summary>
        /// Async scoped tenant operations using WithTenantAsync.
        /// </summary>
        public static Task RunAsyncScopedTenantExample()
        {
            ExampleOutput.WriteLine("=== Async Scoped Tenant Example ===\n");

            var tenantGuid = Guid.NewGuid();
            ITenantContext context = new TenantContext();

            // WithTenantAsync for async operations
            // var data = await context.WithTenantAsync<List<Document>>(tenantGuid, "Acme", async () =>
            // {
            //     return await asyncStore.ReadAsync(null);
            // });

            ExampleOutput.WriteLine("WithTenantAsync<TResult> pattern:");
            ExampleOutput.WriteLine("  var result = await context.WithTenantAsync<MyData>(tenantGuid, name, async () =>");
            ExampleOutput.WriteLine("  {");
            ExampleOutput.WriteLine("      // Tenant is set for the duration of this async block");
            ExampleOutput.WriteLine("      return await store.ReadAsync(filter);");
            ExampleOutput.WriteLine("  });");
            ExampleOutput.WriteLine("  // Previous tenant is restored after await completes");

            ExampleOutput.WriteLine("\nWithTenantAsync (void) pattern:");
            ExampleOutput.WriteLine("  await context.WithTenantAsync(tenantGuid, name, async () =>");
            ExampleOutput.WriteLine("  {");
            ExampleOutput.WriteLine("      await store.CreateAsync(item);");
            ExampleOutput.WriteLine("  });");

            ExampleOutput.WriteLine("\nAsyncLocal ensures tenant context flows across await boundaries.");

            ExampleOutput.WriteLine("\n=== Example Complete ===");
            return Task.CompletedTask;
        }

        /// <summary>
        /// Non-tenant mode: accessing all data without tenant filtering.
        /// </summary>
        public static Task RunNonTenantModeExample()
        {
            ExampleOutput.WriteLine("=== Non-Tenant Mode Example ===\n");

            ITenantContext context = new TenantContext();

            ExampleOutput.WriteLine($"HasTenant: {context.HasTenant}");
            ExampleOutput.WriteLine("When no tenant is set, TenantStoreWrapper allows access to all items.");
            ExampleOutput.WriteLine("This is useful for admin operations or background jobs.\n");

            // Set tenant for normal operations
            var tenantGuid = Guid.NewGuid();
            context.SetTenant(tenantGuid, "User Tenant");
            ExampleOutput.WriteLine($"Tenant set: {context.CurrentTenantName} - operations are filtered");

            // Clear for admin access
            context.ClearTenant();
            ExampleOutput.WriteLine($"Tenant cleared: HasTenant = {context.HasTenant} - full access restored");

            ExampleOutput.WriteLine("\nPattern for admin operations:");
            ExampleOutput.WriteLine("  context.ClearTenant();");
            ExampleOutput.WriteLine("  var allItems = store.Read(null); // returns all items across tenants");
            ExampleOutput.WriteLine("  context.SetTenant(tenantGuid, name); // restore tenant filtering");

            ExampleOutput.WriteLine("\n=== Example Complete ===");
            return Task.CompletedTask;
        }

        /// <summary>
        /// Tenant-based filtering with ModelByTenant filter.
        /// </summary>
        public static Task RunFilteringExample()
        {
            ExampleOutput.WriteLine("=== Tenant Filtering Example ===\n");

            ExampleOutput.WriteLine("ModelByTenant<T> filter:");
            ExampleOutput.WriteLine("  Combines tenant ID filtering with any additional expression filter.");
            ExampleOutput.WriteLine("  Used internally by TenantStoreWrapper for all Read/Count operations.\n");

            ExampleOutput.WriteLine("Usage in TenantStoreWrapper.Read(filter):");
            ExampleOutput.WriteLine("  1. Takes user-provided filter (e.g., x => x.Name == \"test\")");
            ExampleOutput.WriteLine("  2. Wraps it: new ModelByTenant<T>(tenantGuid, userFilter).Filter()");
            ExampleOutput.WriteLine("  3. Result: x => x.TenantGuid == currentTenantGuid && x.Name == \"test\"");
            ExampleOutput.WriteLine("  4. Passes combined filter to inner store\n");

            ExampleOutput.WriteLine("TenantStoreExtensions:");
            ExampleOutput.WriteLine("  Provides extension methods for wrapping existing stores:");
            ExampleOutput.WriteLine("  var tenantStore = existingStore.AsTenantStore(tenantContext);");

            ExampleOutput.WriteLine("\n=== Example Complete ===");
            return Task.CompletedTask;
        }

        /// <summary>
        /// Tenant security: cross-tenant access prevention.
        /// </summary>
        public static Task RunSecurityExample()
        {
            ExampleOutput.WriteLine("=== Tenant Security Example ===\n");

            var tenantAId = Guid.NewGuid();
            var tenantBId = Guid.NewGuid();

            ExampleOutput.WriteLine($"Tenant A: {tenantAId}");
            ExampleOutput.WriteLine($"Tenant B: {tenantBId}\n");

            ExampleOutput.WriteLine("Security enforcement in TenantStoreWrapper:");
            ExampleOutput.WriteLine("  1. Update/Delete check BelongsToCurrentTenant(item)");
            ExampleOutput.WriteLine("     - Compares item.TenantGuid with context.CurrentTenantGuid");
            ExampleOutput.WriteLine("     - Throws UnauthorizedAccessException on mismatch\n");

            ExampleOutput.WriteLine("  2. Create automatically stamps the item:");
            ExampleOutput.WriteLine("     - item.TenantGuid = context.CurrentTenantGuid");
            ExampleOutput.WriteLine("     - item.TenantName = context.CurrentTenantName\n");

            ExampleOutput.WriteLine("  3. Read uses ModelByTenant<T> filter:");
            ExampleOutput.WriteLine("     - Only returns items where TenantGuid matches current tenant");
            ExampleOutput.WriteLine("     - Impossible to read another tenant's data\n");

            ExampleOutput.WriteLine("Example: Tenant A tries to update Tenant B's item:");
            ExampleOutput.WriteLine("  context.SetTenant(tenantAId, \"A\");");
            ExampleOutput.WriteLine("  tenantStore.Update(itemBelongingToTenantB); // throws UnauthorizedAccessException");

            ExampleOutput.WriteLine("\n=== Example Complete ===");
            return Task.CompletedTask;
        }
    }
}
