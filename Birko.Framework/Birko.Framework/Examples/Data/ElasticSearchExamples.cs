using System;
using System.Linq;
using Birko.Data.ElasticSearch.Stores;

namespace Birko.Framework.Examples.Data;

/// <summary>
/// Examples demonstrating Elasticsearch data access using ElasticSearchStore.
/// </summary>
public static class ElasticSearchExamples
{
    /// <summary>
    /// Basic CRUD and query operations with ElasticSearchStore.
    /// </summary>
    public static void RunBasicExample()
    {
        var settings = new Settings
        {
            Location = "http://localhost:9200",
            Name = "products"
        };

        var store = new ElasticSearchStore<ExampleProduct>();
        store.SetSettings(settings);

        try
        {
            store.Init();

            // Create
            var id = store.Create(new ExampleProduct
            {
                Name = "Search Widget",
                Price = 49.99m,
                Description = "Full-text searchable widget",
                Category = "Electronics",
                StockQuantity = 200
            });
            ExampleOutput.WriteLine($"Created product: {id}");

            // Read by ID
            var product = store.Read(id);
            ExampleOutput.WriteLine($"Read: {product?.Name}");

            // Read with expression filter
            var electronics = store.Read(p => p.Category == "Electronics");
            ExampleOutput.WriteLine($"Found {electronics?.Count()} electronics products");

            // Count
            var count = store.Count();
            ExampleOutput.WriteLine($"Total documents in index: {count}");

            // Delete and Destroy
            if (product != null) store.Delete(product);
            ExampleOutput.WriteLine("Soft-deleted product");

            store.Destroy();
            ExampleOutput.WriteLine("Hard-deleted product");
        }
        catch (Exception ex)
        {
            ExampleOutput.WriteLine($"Expected error (no Elasticsearch running): {ex.Message}");
        }
    }

    /// <summary>
    /// Streaming reads for large datasets using ReadStream.
    /// </summary>
    public static void RunStreamingExample()
    {
        var settings = new Settings
        {
            Location = "http://localhost:9200",
            Name = "products"
        };

        var store = new ElasticSearchStore<ExampleProduct>();
        store.SetSettings(settings);

        try
        {
            store.Init();

            // Seed data
            for (int i = 0; i < 50; i++)
            {
                store.Create(new ExampleProduct
                {
                    Name = $"StreamItem-{i}",
                    Price = 10m + i,
                    Category = "Stream",
                    StockQuantity = i * 10
                });
            }

            // ReadStream processes documents in batches without loading all into memory
            var stream = store.ReadStream(p => p.Category == "Stream");
            var processed = 0;
            foreach (var product in stream)
            {
                processed++;
            }
            ExampleOutput.WriteLine($"Streamed {processed} documents");
        }
        catch (Exception ex)
        {
            ExampleOutput.WriteLine($"Expected error (no Elasticsearch running): {ex.Message}");
        }
    }

    /// <summary>
    /// Health check and index management operations.
    /// </summary>
    public static void RunHealthCheckExample()
    {
        var settings = new Settings
        {
            Location = "http://localhost:9200",
            Name = "products"
        };

        var store = new ElasticSearchStore<ExampleProduct>();
        store.SetSettings(settings);

        try
        {
            store.Init();

            // Health check
            var healthy = store.IsHealthy();
            ExampleOutput.WriteLine($"Cluster healthy: {healthy}");

            // Index info
            var indexName = store.GetIndexName();
            ExampleOutput.WriteLine($"Index name: {indexName}");

            // Cache and index management
            store.ClearCache();
            ExampleOutput.WriteLine("Cleared index cache");

            store.DeleteIndex();
            ExampleOutput.WriteLine("Deleted index");
        }
        catch (Exception ex)
        {
            ExampleOutput.WriteLine($"Expected error (no Elasticsearch running): {ex.Message}");
        }
    }
}
