using Microsoft.Azure.Cosmos;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;

namespace Birko.Data.Migrations.CosmosDB;

/// <summary>
/// Abstract base class for Cosmos DB migrations.
/// Provides helpers for container management, indexing policy changes,
/// document operations, and bulk inserts.
/// </summary>
public abstract class CosmosMigration : Data.Migrations.AbstractMigration
{
    /// <summary>
    /// Applies the migration using the Cosmos DB database.
    /// </summary>
    /// <param name="database">The Cosmos DB database.</param>
    protected abstract void Up(Database database);

    /// <summary>
    /// Reverts the migration using the Cosmos DB database.
    /// </summary>
    /// <param name="database">The Cosmos DB database.</param>
    protected abstract void Down(Database database);

    /// <summary>
    /// Throws exception - migrations require Database context.
    /// </summary>
    public override void Up()
    {
        throw new InvalidOperationException("CosmosMigration requires Database. Use CosmosMigrationRunner to execute migrations.");
    }

    /// <summary>
    /// Throws exception - migrations require Database context.
    /// </summary>
    public override void Down()
    {
        throw new InvalidOperationException("CosmosMigration requires Database. Use CosmosMigrationRunner to execute migrations.");
    }

    /// <summary>
    /// Internal execution method called by CosmosMigrationRunner.
    /// </summary>
    internal void Execute(Database database, Data.Migrations.MigrationDirection direction)
    {
        if (direction == Data.Migrations.MigrationDirection.Up)
        {
            Up(database);
        }
        else
        {
            Down(database);
        }
    }

    #region Container Management

    /// <summary>
    /// Creates a container if it doesn't exist.
    /// </summary>
    protected virtual void CreateContainer(Database database, string containerName, string partitionKeyPath = "/id", int? throughput = null)
    {
        var properties = new ContainerProperties(containerName, partitionKeyPath);
        if (throughput.HasValue)
        {
            database.CreateContainerIfNotExistsAsync(properties, throughput.Value)
                .GetAwaiter().GetResult();
        }
        else
        {
            database.CreateContainerIfNotExistsAsync(properties)
                .GetAwaiter().GetResult();
        }
    }

    /// <summary>
    /// Deletes a container.
    /// </summary>
    protected virtual void DeleteContainer(Database database, string containerName)
    {
        var container = database.GetContainer(containerName);
        container.DeleteContainerAsync().GetAwaiter().GetResult();
    }

    #endregion

    #region Indexing Policy

    /// <summary>
    /// Adds an included path to the indexing policy.
    /// </summary>
    protected virtual void AddIncludedPath(Database database, string containerName, string path)
    {
        var container = database.GetContainer(containerName);
        var response = container.ReadContainerAsync().GetAwaiter().GetResult();
        var properties = response.Resource;

        if (!path.EndsWith("/?"))
        {
            path += "/?";
        }

        if (!properties.IndexingPolicy.IncludedPaths.Any(p => p.Path == path))
        {
            properties.IndexingPolicy.IncludedPaths.Add(new IncludedPath { Path = path });
            container.ReplaceContainerAsync(properties).GetAwaiter().GetResult();
        }
    }

    /// <summary>
    /// Adds an excluded path to the indexing policy.
    /// </summary>
    protected virtual void AddExcludedPath(Database database, string containerName, string path)
    {
        var container = database.GetContainer(containerName);
        var response = container.ReadContainerAsync().GetAwaiter().GetResult();
        var properties = response.Resource;

        if (!properties.IndexingPolicy.ExcludedPaths.Any(p => p.Path == path))
        {
            properties.IndexingPolicy.ExcludedPaths.Add(new ExcludedPath { Path = path });
            container.ReplaceContainerAsync(properties).GetAwaiter().GetResult();
        }
    }

    /// <summary>
    /// Adds a composite index to the indexing policy.
    /// </summary>
    protected virtual void AddCompositeIndex(Database database, string containerName, params (string path, CompositePathSortOrder order)[] paths)
    {
        var container = database.GetContainer(containerName);
        var response = container.ReadContainerAsync().GetAwaiter().GetResult();
        var properties = response.Resource;

        var compositeIndex = new System.Collections.ObjectModel.Collection<CompositePath>();
        foreach (var (path, order) in paths)
        {
            compositeIndex.Add(new CompositePath { Path = path, Order = order });
        }

        properties.IndexingPolicy.CompositeIndexes.Add(compositeIndex);
        container.ReplaceContainerAsync(properties).GetAwaiter().GetResult();
    }

    /// <summary>
    /// Replaces the entire indexing policy for a container.
    /// </summary>
    protected virtual void SetIndexingPolicy(Database database, string containerName, IndexingPolicy policy)
    {
        var container = database.GetContainer(containerName);
        var response = container.ReadContainerAsync().GetAwaiter().GetResult();
        var properties = response.Resource;
        properties.IndexingPolicy = policy;
        container.ReplaceContainerAsync(properties).GetAwaiter().GetResult();
    }

    #endregion

    #region Document Operations

    /// <summary>
    /// Loads a document by ID and partition key.
    /// </summary>
    protected virtual T? LoadDocument<T>(Database database, string containerName, string id, string partitionKey)
    {
        var container = database.GetContainer(containerName);
        try
        {
            var response = container.ReadItemAsync<T>(id, new PartitionKey(partitionKey))
                .GetAwaiter().GetResult();
            return response.Resource;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return default;
        }
    }

    /// <summary>
    /// Stores (upserts) a document.
    /// </summary>
    protected virtual void StoreDocument<T>(Database database, string containerName, T entity, string partitionKey)
    {
        var container = database.GetContainer(containerName);
        container.UpsertItemAsync(entity, new PartitionKey(partitionKey))
            .GetAwaiter().GetResult();
    }

    /// <summary>
    /// Deletes a document by ID and partition key.
    /// </summary>
    protected virtual void DeleteDocument(Database database, string containerName, string id, string partitionKey)
    {
        var container = database.GetContainer(containerName);
        container.DeleteItemAsync<object>(id, new PartitionKey(partitionKey))
            .GetAwaiter().GetResult();
    }

    /// <summary>
    /// Checks if a document exists.
    /// </summary>
    protected virtual bool DocumentExists(Database database, string containerName, string id, string partitionKey)
    {
        var container = database.GetContainer(containerName);
        try
        {
            container.ReadItemAsync<object>(id, new PartitionKey(partitionKey))
                .GetAwaiter().GetResult();
            return true;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }
    }

    /// <summary>
    /// Bulk inserts documents into a container using parallel operations.
    /// </summary>
    protected virtual void BulkInsert<T>(Database database, string containerName, IEnumerable<T> entities, Func<T, string> partitionKeySelector)
    {
        var container = database.GetContainer(containerName);
        var tasks = entities.Select(entity =>
            container.CreateItemAsync(entity, new PartitionKey(partitionKeySelector(entity)))
        ).ToArray();

        Task.WhenAll(tasks).GetAwaiter().GetResult();
    }

    #endregion
}
