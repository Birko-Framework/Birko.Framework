using System;
using System.Collections.Generic;
using System.Linq;
using Birko.Data.MongoDB.Stores;

namespace Birko.Framework.Examples.Data;

/// <summary>
/// Examples demonstrating MongoDB data access using MongoDBStore and AsyncMongoDBStore.
/// </summary>
public static class MongoDbExamples
{
    /// <summary>
    /// Basic CRUD operations with MongoDBStore.
    /// </summary>
    public static void RunBasicCrudExample()
    {
        var store = new MongoDBStore<ExampleUser>();
        store.SetSettings(new Settings("localhost", "app_db", "mongouser", "mongopass"));

        try
        {
            store.Init();

            // Create
            var id = store.Create(new ExampleUser
            {
                Username = "jdoe",
                Email = "jdoe@example.com",
                Age = 30
            });
            ExampleOutput.WriteLine($"Created user: {id}");

            // Read by ID
            var user = store.Read(id);
            ExampleOutput.WriteLine($"Read: {user?.Username} ({user?.Email})");

            // Read with filter
            var adults = store.Read(u => u.Age >= 18);
            ExampleOutput.WriteLine($"Found {adults?.Count()} adults");

            // Update
            if (user != null)
            {
                user.Email = "john.doe@example.com";
                user.Age = 31;
                store.Update(user);
                ExampleOutput.WriteLine("Updated user email and age");
            }

            // Delete (soft) and Destroy (hard)
            if (user != null) store.Delete(user);
            ExampleOutput.WriteLine("Soft-deleted user");

            store.Destroy();
            ExampleOutput.WriteLine("Hard-deleted user");
        }
        catch (Exception ex)
        {
            ExampleOutput.WriteLine($"Expected error (no MongoDB running): {ex.Message}");
        }
    }

    /// <summary>
    /// Bulk operations using MongoDBStore (extends AbstractBulkStore).
    /// </summary>
    public static void RunBulkOperationsExample()
    {
        var store = new MongoDBStore<ExampleUser>();
        store.SetSettings(new Settings("localhost", "app_db", "mongouser", "mongopass"));

        try
        {
            store.Init();

            // Bulk create
            var users = Enumerable.Range(1, 50).Select(i => new ExampleUser
            {
                Username = $"user{i}",
                Email = $"user{i}@example.com",
                Age = 20 + (i % 40)
            });
            store.Create(users);
            ExampleOutput.WriteLine("Bulk created 50 users");

            // Bulk read and update
            var allUsers = store.Read(u => u.Age >= 20);
            if (allUsers != null)
            {
                var updated = allUsers.Select(u =>
                {
                    u.Age += 1;
                    return u;
                });
                store.Update(updated);
                ExampleOutput.WriteLine("Bulk updated user ages");

                // Bulk delete
                store.Delete(allUsers);
                ExampleOutput.WriteLine("Bulk soft-deleted all users");
            }
        }
        catch (Exception ex)
        {
            ExampleOutput.WriteLine($"Expected error (no MongoDB running): {ex.Message}");
        }
    }

    /// <summary>
    /// Various MongoDB connection configurations.
    /// </summary>
    public static void ShowConnectionConfiguration()
    {
        // Basic local connection
        var local = new Settings("localhost", "mydb", "", "");
        ExampleOutput.WriteLine($"Local: {local.GetConnectionString()}");

        // Authenticated connection
        var auth = new Settings("mongo-server.local", "production_db", "admin", "s3cret")
        {
            Port = 27017,
            AuthDatabase = "admin"
        };
        ExampleOutput.WriteLine($"Authenticated: {auth.GetConnectionString()}");

        // Replica set with TLS
        var replica = new Settings("mongo-rs.local", "ha_db", "admin", "s3cret")
        {
            Port = 27017,
            ReplicaSet = "rs0",
            UseSecure = true,
            AuthDatabase = "admin"
        };
        ExampleOutput.WriteLine($"Replica set: {replica.GetConnectionString()}");
    }
}
