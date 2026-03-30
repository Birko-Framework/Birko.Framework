using System;
using System.Linq;
using Birko.Data.JSON.Stores;
using Birko.Data.Stores;
using Birko.Configuration;

namespace Birko.Framework.Examples.Data;

/// <summary>
/// Examples demonstrating JSON file-based storage using JsonStore.
/// </summary>
public static class JsonStoreExamples
{
    /// <summary>
    /// Basic CRUD operations with JsonStore.
    /// </summary>
    public static void RunBasicExample()
    {
        var store = new JsonStore<ExampleProduct>();
        store.SetSettings(new Settings("./data", "products"));

        try
        {
            store.Init();

            // Create
            var id = store.Create(new ExampleProduct
            {
                Name = "JSON Widget",
                Price = 19.99m,
                Description = "Stored as JSON on disk",
                Category = "Prototyping",
                StockQuantity = 50
            });
            ExampleOutput.WriteLine($"Created: {id}");

            // Read by ID
            var product = store.Read(id);
            ExampleOutput.WriteLine($"Read: {product?.Name} - ${product?.Price}");

            // Read with filter
            var cheap = store.Read(p => p.Price < 25m);
            ExampleOutput.WriteLine($"Found {cheap?.Count()} products under $25");

            // Update
            if (product != null)
            {
                product.Price = 24.99m;
                store.Update(product);
                ExampleOutput.WriteLine("Updated price");
            }

            // Count
            var count = store.Count();
            ExampleOutput.WriteLine($"Total stored: {count}");

            // Delete and Destroy
            if (product != null) store.Delete(product);
            store.Destroy();
            ExampleOutput.WriteLine("Deleted product");
        }
        catch (Exception ex)
        {
            ExampleOutput.WriteLine($"Error: {ex.Message}");
        }
    }

    /// <summary>
    /// Multiple JsonStore instances for different data types.
    /// </summary>
    public static void RunMultiStoreExample()
    {
        var productStore = new JsonStore<ExampleProduct>();
        productStore.SetSettings(new Settings("./data", "products"));

        var userStore = new JsonStore<ExampleUser>();
        userStore.SetSettings(new Settings("./data", "users"));

        try
        {
            productStore.Init();
            userStore.Init();

            // Each store manages its own JSON files
            productStore.Create(new ExampleProduct
            {
                Name = "Multi-Store Widget",
                Price = 15m,
                Category = "Demo",
                StockQuantity = 10
            });

            userStore.Create(new ExampleUser
            {
                Username = "demo_user",
                Email = "demo@example.com",
                Age = 25
            });

            ExampleOutput.WriteLine($"Products: {productStore.Count()}, Users: {userStore.Count()}");
        }
        catch (Exception ex)
        {
            ExampleOutput.WriteLine($"Error: {ex.Message}");
        }
    }

    /// <summary>
    /// Prototyping workflow showing file path info and quick data setup.
    /// </summary>
    public static void RunPrototypingExample()
    {
        var store = new JsonStore<ExampleProduct>();
        store.SetSettings(new Settings("./prototype-data", "inventory"));

        try
        {
            store.Init();

            // File path information
            ExampleOutput.WriteLine($"Store path: {store.Path}");
            ExampleOutput.WriteLine($"Directory: {store.PathDirectory}");

            // Rapid data seeding for prototyping
            var categories = new[] { "Electronics", "Books", "Clothing", "Food", "Tools" };
            foreach (var (category, index) in categories.Select((c, i) => (c, i)))
            {
                store.Create(new ExampleProduct
                {
                    Name = $"Sample {category} Item",
                    Price = 10m * (index + 1),
                    Category = category,
                    StockQuantity = 100
                });
            }

            ExampleOutput.WriteLine($"Seeded {store.Count()} prototype items");

            // Query by category
            var electronics = store.Read(p => p.Category == "Electronics");
            ExampleOutput.WriteLine($"Electronics: {electronics?.Count()}");
        }
        catch (Exception ex)
        {
            ExampleOutput.WriteLine($"Error: {ex.Message}");
        }
    }
}
