using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.Stores;
using Birko.Data.Stores;
using Birko.Configuration;

namespace Birko.Framework.Examples.Data;

/// <summary>
/// Examples demonstrating SQL data access using AsyncDataBaseStore and DataBaseStore
/// with PostgreSQL, MSSql, MySQL, and SQLite connectors.
/// </summary>
public static class SqlExamples
{
    /// <summary>
    /// Basic CRUD operations using AsyncDataBaseStore with PostgreSQL.
    /// </summary>
    public static async Task RunBasicCrudExample()
    {
        var store = new AsyncDataBaseStore<PostgreSQLConnector, ExampleProduct>();
        store.SetSettings(new RemoteSettings("localhost", "shop_db", "admin", "secret", 5432));

        try
        {
            await store.InitAsync();

            // Create
            var id = await store.CreateAsync(new ExampleProduct
            {
                Name = "Widget",
                Price = 29.99m,
                Description = "A standard widget",
                Category = "Parts",
                StockQuantity = 100
            });
            ExampleOutput.WriteLine($"Created product: {id}");

            // Read by ID
            var product = await store.ReadAsync(id);
            ExampleOutput.WriteLine($"Read: {product?.Name} - ${product?.Price}");

            // Read with filter
            var expensiveCount = await store.CountAsync(p => p.Price > 20m);
            ExampleOutput.WriteLine($"Found {expensiveCount} products over $20");

            // Update
            if (product != null)
            {
                product.Price = 34.99m;
                product.StockQuantity = 85;
                await store.UpdateAsync(product);
                ExampleOutput.WriteLine("Updated price and stock");
            }

            // Delete (soft) and Destroy (hard)
            if (product != null) await store.DeleteAsync(product);
            ExampleOutput.WriteLine("Soft-deleted product");

            await store.DestroyAsync();
            ExampleOutput.WriteLine("Hard-deleted product");
        }
        catch (Exception ex)
        {
            ExampleOutput.WriteLine($"Expected error (no DB running): {ex.Message}");
        }
    }

    /// <summary>
    /// Parallel async operations demonstrating concurrency benefits.
    /// </summary>
    public static async Task RunAsyncOperationsExample()
    {
        var store = new AsyncDataBaseStore<PostgreSQLConnector, ExampleProduct>();
        store.SetSettings(new RemoteSettings("localhost", "shop_db", "admin", "secret", 5432));

        try
        {
            await store.InitAsync();

            // Parallel creates
            var tasks = Enumerable.Range(1, 10).Select(i =>
                store.CreateAsync(new ExampleProduct
                {
                    Name = $"Product-{i}",
                    Price = 10m * i,
                    Category = "Bulk",
                    StockQuantity = i * 50
                })
            );

            var ids = await Task.WhenAll(tasks);
            ExampleOutput.WriteLine($"Created {ids.Length} products in parallel");

            // Parallel reads
            var readTasks = ids.Select(id => store.ReadAsync(id));
            var products = await Task.WhenAll(readTasks);
            ExampleOutput.WriteLine($"Read {products.Count(p => p != null)} products in parallel");
        }
        catch (Exception ex)
        {
            ExampleOutput.WriteLine($"Expected error (no DB running): {ex.Message}");
        }
    }

    /// <summary>
    /// Bulk operations using AsyncDataBaseBulkStore for batch processing.
    /// </summary>
    public static async Task RunBulkOperationsExample()
    {
        var store = new AsyncDataBaseBulkStore<PostgreSQLConnector, ExampleProduct>();
        store.SetSettings(new RemoteSettings("localhost", "shop_db", "admin", "secret", 5432));

        try
        {
            await store.InitAsync();

            // Bulk create
            var products = Enumerable.Range(1, 100).Select(i => new ExampleProduct
            {
                Name = $"BulkItem-{i}",
                Price = 5m + (i * 0.5m),
                Category = "Wholesale",
                StockQuantity = 1000
            });
            await store.CreateAsync(products);
            ExampleOutput.WriteLine("Bulk created 100 products");

            // Bulk read and update
            var all = await store.ReadAsync(p => p.Category == "Wholesale");
            if (all != null)
            {
                var updated = all.Select(p =>
                {
                    p.Price *= 1.1m; // 10% price increase
                    return p;
                });
                await store.UpdateAsync(updated);
                ExampleOutput.WriteLine("Bulk updated prices");

                // Bulk delete
                await store.DeleteAsync(all);
                ExampleOutput.WriteLine("Bulk soft-deleted all wholesale products");
            }
        }
        catch (Exception ex)
        {
            ExampleOutput.WriteLine($"Expected error (no DB running): {ex.Message}");
        }
    }

    /// <summary>
    /// Connection settings for each supported SQL provider.
    /// </summary>
    public static void ShowProviderConfiguration()
    {
        // PostgreSQL
        var pgSettings = new RemoteSettings("pg-server.local", "mydb", "pguser", "pgpass", 5432);
        var pgStore = new AsyncDataBaseStore<PostgreSQLConnector, ExampleProduct>();
        pgStore.SetSettings(pgSettings);

        // Microsoft SQL Server
        var msSettings = new RemoteSettings("sql-server.local", "mydb", "sa", "sapass", 1433);
        var msStore = new AsyncDataBaseStore<MSSqlConnector, ExampleProduct>();
        msStore.SetSettings(msSettings);

        // MySQL
        var mySettings = new RemoteSettings("mysql-server.local", "mydb", "root", "rootpass", 3306);
        var myStore = new AsyncDataBaseStore<MySQLConnector, ExampleProduct>();
        myStore.SetSettings(mySettings);

        // SQLite (file-based, uses PasswordSettings)
        var sqliteSettings = new PasswordSettings("./data", "local.db", "optional-password");
        var sqliteStore = new DataBaseStore<SqLiteConnector, ExampleProduct>();
        sqliteStore.SetSettings(sqliteSettings);

        ExampleOutput.WriteLine("Configured stores: PostgreSQL, MSSql, MySQL, SQLite");
    }
}
