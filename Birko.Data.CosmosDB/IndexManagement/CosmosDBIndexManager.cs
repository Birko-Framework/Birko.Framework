using Microsoft.Azure.Cosmos;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IndexDefinition = Birko.Data.Patterns.IndexManagement.IndexDefinition;
using IndexField = Birko.Data.Patterns.IndexManagement.IndexField;
using IndexFieldType = Birko.Data.Patterns.IndexManagement.IndexFieldType;
using IndexInfo = Birko.Data.Patterns.IndexManagement.IndexInfo;
using IndexManagementException = Birko.Data.Patterns.IndexManagement.IndexManagementException;
using IIndexManager = Birko.Data.Patterns.IndexManagement.IIndexManager;

namespace Birko.Data.CosmosDB.IndexManagement;

/// <summary>
/// Cosmos DB implementation of <see cref="IIndexManager"/>.
/// Manages indexing policies on Cosmos DB containers.
/// Scope parameter maps to the container name.
/// </summary>
public class CosmosDBIndexManager : IIndexManager
{
    private readonly Database _database;

    public CosmosDBIndexManager(Database database)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
    }

    /// <inheritdoc />
    public async Task<bool> ExistsAsync(string indexName, string? scope = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(indexName)) throw new ArgumentException("Index name is required.", nameof(indexName));
        if (string.IsNullOrWhiteSpace(scope)) throw new ArgumentException("Scope (container name) is required for Cosmos DB.", nameof(scope));

        var container = _database.GetContainer(scope);
        var containerProperties = await container.ReadContainerAsync(cancellationToken: ct).ConfigureAwait(false);
        var policy = containerProperties.Resource.IndexingPolicy;

        return policy.IncludedPaths.Any(p => p.Path == indexName)
            || policy.CompositeIndexes.Any(ci => ci.Any(e => e.Path == indexName));
    }

    /// <inheritdoc />
    public async Task CreateAsync(IndexDefinition definition, string? scope = null, CancellationToken ct = default)
    {
        if (definition == null) throw new ArgumentNullException(nameof(definition));
        if (string.IsNullOrWhiteSpace(definition.Name)) throw new ArgumentException("Index name is required.", nameof(definition));
        if (string.IsNullOrWhiteSpace(scope)) throw new ArgumentException("Scope (container name) is required for Cosmos DB.", nameof(scope));

        var container = _database.GetContainer(scope);

        try
        {
            var containerResponse = await container.ReadContainerAsync(cancellationToken: ct).ConfigureAwait(false);
            var properties = containerResponse.Resource;
            var policy = properties.IndexingPolicy;

            if (definition.Fields.Count == 1)
            {
                var field = definition.Fields[0];
                var path = field.Name.StartsWith("/") ? field.Name : $"/{field.Name}";
                if (!path.EndsWith("/?"))
                {
                    path += "/?";
                }

                if (!policy.IncludedPaths.Any(p => p.Path == path))
                {
                    policy.IncludedPaths.Add(new IncludedPath { Path = path });
                }
            }
            else if (definition.Fields.Count > 1)
            {
                var compositeIndex = new System.Collections.ObjectModel.Collection<CompositePath>();
                foreach (var field in definition.Fields)
                {
                    var path = field.Name.StartsWith("/") ? field.Name : $"/{field.Name}";
                    compositeIndex.Add(new CompositePath
                    {
                        Path = path,
                        Order = field.IsDescending
                            ? CompositePathSortOrder.Descending
                            : CompositePathSortOrder.Ascending
                    });
                }
                policy.CompositeIndexes.Add(compositeIndex);
            }

            await container.ReplaceContainerAsync(properties, cancellationToken: ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not ArgumentException)
        {
            throw new IndexManagementException(
                $"Failed to create index '{definition.Name}' on container '{scope}'.",
                definition.Name, scope, ex);
        }
    }

    /// <inheritdoc />
    public async Task DropAsync(string indexName, string? scope = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(indexName)) throw new ArgumentException("Index name is required.", nameof(indexName));
        if (string.IsNullOrWhiteSpace(scope)) throw new ArgumentException("Scope (container name) is required for Cosmos DB.", nameof(scope));

        var container = _database.GetContainer(scope);

        try
        {
            var containerResponse = await container.ReadContainerAsync(cancellationToken: ct).ConfigureAwait(false);
            var properties = containerResponse.Resource;
            var policy = properties.IndexingPolicy;

            var toRemove = policy.IncludedPaths.FirstOrDefault(p => p.Path == indexName);
            if (toRemove != null)
            {
                policy.IncludedPaths.Remove(toRemove);
            }

            if (!string.IsNullOrWhiteSpace(indexName))
            {
                policy.ExcludedPaths.Add(new ExcludedPath { Path = indexName });
            }

            await container.ReplaceContainerAsync(properties, cancellationToken: ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not ArgumentException)
        {
            throw new IndexManagementException(
                $"Failed to drop index '{indexName}' on container '{scope}'.",
                indexName, scope, ex);
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<IndexInfo>> ListAsync(string? scope = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(scope)) throw new ArgumentException("Scope (container name) is required for Cosmos DB.", nameof(scope));

        var container = _database.GetContainer(scope);
        var containerResponse = await container.ReadContainerAsync(cancellationToken: ct).ConfigureAwait(false);
        var policy = containerResponse.Resource.IndexingPolicy;

        var results = new List<IndexInfo>();

        foreach (var path in policy.IncludedPaths)
        {
            results.Add(new IndexInfo
            {
                Name = path.Path,
                State = "active",
                Properties = new Dictionary<string, object>
                {
                    ["Type"] = "IncludedPath",
                    ["IndexingMode"] = policy.IndexingMode.ToString()
                }
            });
        }

        for (int i = 0; i < policy.CompositeIndexes.Count; i++)
        {
            var composite = policy.CompositeIndexes[i];
            var paths = string.Join(", ", composite.Select(c => $"{c.Path} {c.Order}"));
            results.Add(new IndexInfo
            {
                Name = $"composite_{i}",
                State = "active",
                Properties = new Dictionary<string, object>
                {
                    ["Type"] = "CompositeIndex",
                    ["Paths"] = paths
                }
            });
        }

        return results;
    }

    /// <inheritdoc />
    public async Task<IndexInfo?> GetInfoAsync(string indexName, string? scope = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(indexName)) throw new ArgumentException("Index name is required.", nameof(indexName));

        var all = await ListAsync(scope, ct).ConfigureAwait(false);
        return all.FirstOrDefault(i => i.Name == indexName);
    }

    #region Cosmos DB-specific extensions

    /// <summary>
    /// Gets the full indexing policy for a container.
    /// </summary>
    /// <param name="containerName">The container name.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task<IndexingPolicy> GetIndexingPolicyAsync(string containerName, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(containerName)) throw new ArgumentException("Container name is required.", nameof(containerName));

        var container = _database.GetContainer(containerName);
        var response = await container.ReadContainerAsync(cancellationToken: ct).ConfigureAwait(false);
        return response.Resource.IndexingPolicy;
    }

    /// <summary>
    /// Replaces the entire indexing policy for a container.
    /// </summary>
    /// <param name="containerName">The container name.</param>
    /// <param name="policy">The new indexing policy.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task SetIndexingPolicyAsync(string containerName, IndexingPolicy policy, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(containerName)) throw new ArgumentException("Container name is required.", nameof(containerName));
        if (policy == null) throw new ArgumentNullException(nameof(policy));

        var container = _database.GetContainer(containerName);
        var response = await container.ReadContainerAsync(cancellationToken: ct).ConfigureAwait(false);
        var properties = response.Resource;
        properties.IndexingPolicy = policy;
        await container.ReplaceContainerAsync(properties, cancellationToken: ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Adds a spatial index for a specific path.
    /// </summary>
    /// <param name="containerName">The container name.</param>
    /// <param name="path">The path to add a spatial index for (e.g., "/location/*").</param>
    /// <param name="spatialType">The spatial type (default: Point).</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task AddSpatialIndexAsync(string containerName, string path, SpatialType spatialType = SpatialType.Point, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(containerName)) throw new ArgumentException("Container name is required.", nameof(containerName));
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Path is required.", nameof(path));

        var container = _database.GetContainer(containerName);
        var response = await container.ReadContainerAsync(cancellationToken: ct).ConfigureAwait(false);
        var properties = response.Resource;

        var spatialPath = new SpatialPath { Path = path };
        spatialPath.SpatialTypes.Add(spatialType);
        properties.IndexingPolicy.SpatialIndexes.Add(spatialPath);

        await container.ReplaceContainerAsync(properties, cancellationToken: ct).ConfigureAwait(false);
    }

    #endregion
}
