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

        return PolicyContainsIndex(policy, indexName);
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
                var path = NormalizeIncludedPath(definition.Fields[0].Name);

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
                    var path = NormalizeFieldPath(field.Name);
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

            RemoveIncludedIndex(policy, indexName);

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
        return all.FirstOrDefault(i =>
            i.Name == indexName
            || i.Name == NormalizeIncludedPath(indexName)
            || i.Name == NormalizeFieldPath(indexName));
    }

    #region Path normalization / policy matching (shared by Create/Exists/Drop/GetInfo — CR-M085)

    /// <summary>
    /// Normalizes a single-field included-path index name to the stored Cosmos form, e.g.
    /// <c>Foo</c> or <c>/Foo</c> → <c>/Foo/?</c>. This is the form <see cref="CreateAsync"/> persists.
    /// </summary>
    internal static string NormalizeIncludedPath(string name)
    {
        var path = name.StartsWith("/") ? name : $"/{name}";
        if (!path.EndsWith("/?"))
        {
            path += "/?";
        }
        return path;
    }

    /// <summary>
    /// Normalizes a composite-index field name to the stored Cosmos form, e.g. <c>Foo</c> → <c>/Foo</c>.
    /// </summary>
    internal static string NormalizeFieldPath(string name)
        => name.StartsWith("/") ? name : $"/{name}";

    /// <summary>
    /// True when a stored single-field included path corresponds to the requested index name,
    /// accepting either the raw name or its normalized <c>/name/?</c> form.
    /// </summary>
    internal static bool IncludedPathMatches(string storedPath, string indexName)
        => storedPath == indexName || storedPath == NormalizeIncludedPath(indexName);

    /// <summary>
    /// True when a stored composite field path corresponds to the requested index name,
    /// accepting either the raw name or its normalized <c>/name</c> form.
    /// </summary>
    internal static bool FieldPathMatches(string storedPath, string indexName)
        => storedPath == indexName || storedPath == NormalizeFieldPath(indexName);

    /// <summary>
    /// True when the policy contains an index matching <paramref name="indexName"/> as either a
    /// single-field included path or a composite-index element.
    /// </summary>
    internal static bool PolicyContainsIndex(IndexingPolicy policy, string indexName)
        => policy.IncludedPaths.Any(p => IncludedPathMatches(p.Path, indexName))
        || policy.CompositeIndexes.Any(ci => ci.Any(e => FieldPathMatches(e.Path, indexName)));

    /// <summary>
    /// Removes the single-field included path matching <paramref name="indexName"/> and mirrors the
    /// removed path into <see cref="IndexingPolicy.ExcludedPaths"/>. Returns the removed path, or null
    /// if nothing matched — in which case nothing is excluded (CR-M085: the old code unconditionally
    /// pushed the raw, un-normalized name into ExcludedPaths even when nothing was removed).
    /// </summary>
    internal static string? RemoveIncludedIndex(IndexingPolicy policy, string indexName)
    {
        var toRemove = policy.IncludedPaths.FirstOrDefault(p => IncludedPathMatches(p.Path, indexName));
        if (toRemove == null)
        {
            return null;
        }
        policy.IncludedPaths.Remove(toRemove);
        policy.ExcludedPaths.Add(new ExcludedPath { Path = toRemove.Path });
        return toRemove.Path;
    }

    #endregion

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
