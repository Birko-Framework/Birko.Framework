using Microsoft.Azure.Cosmos;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;
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
/// <remarks>
/// <para>
/// <b>Index names live in a registry (TASK-628, SH-M433).</b> A Cosmos indexing policy has no index names: an index is an
/// included path or a composite path list. <see cref="CreateAsync"/> used to discard <see cref="IndexDefinition.Name"/>, so
/// <c>ExistsAsync("IX_Code")</c> was false for the index it had just made, a composite could never be dropped, and the
/// listing named composites by position. The name now maps to its paths in one document per indexed container, kept in
/// <see cref="RegistryContainerName"/> in the same database. The indexing policy stays the truth: a registered index
/// exists only while its paths are in the policy. A name nothing registered is still answered by path
/// (<c>Code</c>, <c>/Code/?</c>), as the listing reports unregistered policy entries.
/// </para>
/// <para>
/// <b>Flags are honoured or refused, never dropped.</b> <see cref="IndexDefinition.Unique"/> is refused: a Cosmos unique
/// key is enforced within a logical partition, and the Birko stores partition on <c>/id</c> (TASK-614), so every document
/// is its own partition and a unique key rejects nothing. Measured on the emulator: a container created with the unique
/// key <c>/Code</c> accepted a second document with the same <c>Code</c> (201). <see cref="IndexDefinition.Sparse"/>
/// (Cosmos has no sparse index), <see cref="IndexDefinition.ExpireAfter"/> (TTL is a container property), a non-standard
/// field type, a single descending field (a range index has no direction) and any <see cref="IndexDefinition.Properties"/>
/// are refused too.
/// </para>
/// </remarks>
public class CosmosDBIndexManager : IIndexManager
{
    /// <summary>The container holding the index-name registry, one document per indexed container.</summary>
    public const string RegistryContainerName = "__birko_indexes";

    private const string KindIncluded = "included";
    private const string KindComposite = "composite";
    private const int MaxRegistryAttempts = 8;

    private readonly Database _database;

    public CosmosDBIndexManager(Database database)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
    }

    /// <summary>A registered index: the paths a name stands for.</summary>
    private sealed record RegisteredIndex(string Kind, IReadOnlyList<string> Paths, IReadOnlyList<string> Orders);

    /// <inheritdoc />
    public async Task<bool> ExistsAsync(string indexName, string? scope = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(indexName)) throw new ArgumentException("Index name is required.", nameof(indexName));
        if (string.IsNullOrWhiteSpace(scope)) throw new ArgumentException("Scope (container name) is required for Cosmos DB.", nameof(scope));

        var container = _database.GetContainer(scope);
        var containerProperties = await container.ReadContainerAsync(cancellationToken: ct).ConfigureAwait(false);
        var policy = containerProperties.Resource.IndexingPolicy;

        var registered = (await ReadRegistryAsync(scope, ct).ConfigureAwait(false)).Indexes;
        return registered.TryGetValue(indexName, out var entry)
            ? IsInPolicy(entry, policy)
            : PolicyContainsIndex(policy, indexName);
    }

    /// <inheritdoc />
    public async Task CreateAsync(IndexDefinition definition, string? scope = null, CancellationToken ct = default)
    {
        if (definition == null) throw new ArgumentNullException(nameof(definition));
        if (string.IsNullOrWhiteSpace(definition.Name)) throw new ArgumentException("Index name is required.", nameof(definition));
        if (string.IsNullOrWhiteSpace(scope)) throw new ArgumentException("Scope (container name) is required for Cosmos DB.", nameof(scope));
        if (definition.Fields == null || definition.Fields.Count == 0)
            throw new ArgumentException($"Index '{definition.Name}' declares no field.", nameof(definition));
        RefuseUnsupported(definition);

        var entry = ToEntry(definition);
        var container = _database.GetContainer(scope);

        try
        {
            var registry = await ReadRegistryAsync(scope, ct).ConfigureAwait(false);
            if (registry.Indexes.TryGetValue(definition.Name, out var existing) && !SameIndex(existing, entry))
            {
                throw new IndexManagementException(
                    $"Index '{definition.Name}' already exists on container '{scope}' with other fields; drop it first.",
                    definition.Name, scope);
            }

            var properties = (await container.ReadContainerAsync(cancellationToken: ct).ConfigureAwait(false)).Resource;
            if (!IsInPolicy(entry, properties.IndexingPolicy))
            {
                AddToPolicy(entry, properties.IndexingPolicy);
                await container.ReplaceContainerAsync(properties, cancellationToken: ct).ConfigureAwait(false);
            }

            await UpdateRegistryAsync(scope, indexes => indexes[definition.Name] = entry, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not ArgumentException and not NotSupportedException and not IndexManagementException)
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
            var registry = await ReadRegistryAsync(scope, ct).ConfigureAwait(false);
            var containerResponse = await container.ReadContainerAsync(cancellationToken: ct).ConfigureAwait(false);
            var properties = containerResponse.Resource;
            var policy = properties.IndexingPolicy;

            if (!registry.Indexes.TryGetValue(indexName, out var entry))
            {
                if (RemoveIncludedIndex(policy, indexName) != null)
                {
                    await container.ReplaceContainerAsync(properties, cancellationToken: ct).ConfigureAwait(false);
                }
                return;
            }

            // Another registered name may stand for the same paths; those stay in the policy.
            var shared = registry.Indexes.Any(kv => kv.Key != indexName && SameIndex(kv.Value, entry));
            if (!shared && RemoveFromPolicy(entry, policy))
            {
                await container.ReplaceContainerAsync(properties, cancellationToken: ct).ConfigureAwait(false);
            }

            await UpdateRegistryAsync(scope, indexes => indexes.Remove(indexName), ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not ArgumentException and not NotSupportedException)
        {
            throw new IndexManagementException(
                $"Failed to drop index '{indexName}' on container '{scope}'.",
                indexName, scope, ex);
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// Registered indexes by name, then every policy entry no registered index claims, by its path: an included path as
    /// stored (<c>/Code/?</c>), a composite as its path list (<c>(/A ASC, /B DESC)</c>), which does not shift when another
    /// is removed. A registered index whose paths left the policy is not listed: it does not exist.
    /// </remarks>
    public async Task<IReadOnlyList<IndexInfo>> ListAsync(string? scope = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(scope)) throw new ArgumentException("Scope (container name) is required for Cosmos DB.", nameof(scope));

        var container = _database.GetContainer(scope);
        var containerResponse = await container.ReadContainerAsync(cancellationToken: ct).ConfigureAwait(false);
        var policy = containerResponse.Resource.IndexingPolicy;
        var registry = (await ReadRegistryAsync(scope, ct).ConfigureAwait(false)).Indexes;

        var results = new List<IndexInfo>();
        var claimedIncluded = new HashSet<string>(StringComparer.Ordinal);
        var claimedComposite = new HashSet<string>(StringComparer.Ordinal);

        foreach (var (name, entry) in registry.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            if (!IsInPolicy(entry, policy)) continue;
            if (entry.Kind == KindIncluded) claimedIncluded.Add(entry.Paths[0]);
            else claimedComposite.Add(CompositeKey(entry.Paths, entry.Orders));

            results.Add(new IndexInfo
            {
                Name = name,
                State = "active",
                Fields = ToFields(entry),
                Properties = new Dictionary<string, object>
                {
                    ["Type"] = entry.Kind == KindIncluded ? "IncludedPath" : "CompositeIndex",
                    ["Paths"] = string.Join(", ", entry.Paths),
                    ["IndexingMode"] = policy.IndexingMode.ToString()
                }
            });
        }

        foreach (var path in policy.IncludedPaths)
        {
            if (claimedIncluded.Contains(path.Path)) continue;
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

        foreach (var composite in policy.CompositeIndexes)
        {
            if (claimedComposite.Contains(CompositeKey(composite))) continue;
            results.Add(new IndexInfo
            {
                Name = "(" + string.Join(", ", composite.Select(c => $"{c.Path} {(c.Order == CompositePathSortOrder.Descending ? "DESC" : "ASC")}")) + ")",
                State = "active",
                Fields = composite.Select(c => new IndexField { Name = c.Path.TrimStart('/'), IsDescending = c.Order == CompositePathSortOrder.Descending }).ToList(),
                Properties = new Dictionary<string, object>
                {
                    ["Type"] = "CompositeIndex",
                    ["Paths"] = string.Join(", ", composite.Select(c => $"{c.Path} {c.Order}"))
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
        return all.FirstOrDefault(i => i.Name == indexName)
            ?? all.FirstOrDefault(i =>
                i.Name == NormalizeIncludedPath(indexName)
                || i.Name == NormalizeFieldPath(indexName));
    }

    #region Registry

    private sealed class Registry
    {
        public Registry(Dictionary<string, RegisteredIndex> indexes, string? etag)
        {
            Indexes = indexes;
            ETag = etag;
        }

        public Dictionary<string, RegisteredIndex> Indexes { get; }
        public string? ETag { get; }
    }

    private async Task<Container> RegistryContainerAsync(CancellationToken ct)
    {
        var response = await _database.CreateContainerIfNotExistsAsync(
            new ContainerProperties(RegistryContainerName, "/id"), cancellationToken: ct).ConfigureAwait(false);
        return response.Container;
    }

    // Read and written as raw JSON through the stream API, so the registry does not depend on the serializer a
    // consumer configured on the client (Birko configures CosmosGuidIdSerializer).
    private async Task<Registry> ReadRegistryAsync(string scope, CancellationToken ct)
    {
        var registry = await RegistryContainerAsync(ct).ConfigureAwait(false);
        using var response = await registry.ReadItemStreamAsync(scope, new PartitionKey(scope), cancellationToken: ct).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return new Registry(new Dictionary<string, RegisteredIndex>(StringComparer.Ordinal), null);
        }
        response.EnsureSuccessStatusCode();

        using var reader = new StreamReader(response.Content, Encoding.UTF8);
        var json = JsonNode.Parse(await reader.ReadToEndAsync(ct).ConfigureAwait(false));
        var indexes = new Dictionary<string, RegisteredIndex>(StringComparer.Ordinal);
        if (json?["indexes"] is JsonObject map)
        {
            foreach (var (name, node) in map)
            {
                if (node is not JsonObject item) continue;
                indexes[name] = new RegisteredIndex(
                    (string?)item["kind"] ?? KindIncluded,
                    (item["paths"] as JsonArray)?.Select(p => (string)p!).ToList() ?? new List<string>(),
                    (item["orders"] as JsonArray)?.Select(p => (string)p!).ToList() ?? new List<string>());
            }
        }
        return new Registry(indexes, response.Headers.ETag);
    }

    /// <summary>Applies <paramref name="change"/> to the container's registry document, retrying on a concurrent write.</summary>
    private async Task UpdateRegistryAsync(string scope, Action<Dictionary<string, RegisteredIndex>> change, CancellationToken ct)
    {
        var container = await RegistryContainerAsync(ct).ConfigureAwait(false);
        for (var attempt = 1; ; attempt++)
        {
            var registry = await ReadRegistryAsync(scope, ct).ConfigureAwait(false);
            change(registry.Indexes);

            var map = new JsonObject();
            foreach (var (name, entry) in registry.Indexes.OrderBy(kv => kv.Key, StringComparer.Ordinal))
            {
                map[name] = new JsonObject
                {
                    ["kind"] = entry.Kind,
                    ["paths"] = new JsonArray(entry.Paths.Select(p => (JsonNode?)JsonValue.Create(p)).ToArray()),
                    ["orders"] = new JsonArray(entry.Orders.Select(o => (JsonNode?)JsonValue.Create(o)).ToArray()),
                };
            }
            var document = new JsonObject { ["id"] = scope, ["indexes"] = map };
            using var body = new MemoryStream(Encoding.UTF8.GetBytes(document.ToJsonString()));

            using var response = registry.ETag == null
                ? await container.CreateItemStreamAsync(body, new PartitionKey(scope), cancellationToken: ct).ConfigureAwait(false)
                : await container.ReplaceItemStreamAsync(body, scope, new PartitionKey(scope),
                    new ItemRequestOptions { IfMatchEtag = registry.ETag }, ct).ConfigureAwait(false);

            if (response.IsSuccessStatusCode) return;
            var raced = response.StatusCode is HttpStatusCode.PreconditionFailed or HttpStatusCode.Conflict;
            if (!raced || attempt >= MaxRegistryAttempts) response.EnsureSuccessStatusCode();
        }
    }

    #endregion

    #region Definition and policy

    private static void RefuseUnsupported(IndexDefinition definition)
    {
        if (definition.Unique)
            throw new NotSupportedException(
                $"Index '{definition.Name}' is Unique, which Cosmos DB cannot enforce for the Birko stores: a unique key holds "
                + "within a logical partition, and the stores partition on /id, so every document is its own partition and the "
                + "key rejects nothing. Enforce uniqueness in the application (a reservation document whose id is the value).");
        if (definition.Sparse)
            throw new NotSupportedException($"Cosmos DB has no sparse index; '{definition.Name}' cannot be created Sparse.");
        if (definition.ExpireAfter.HasValue)
            throw new NotSupportedException(
                $"A Cosmos DB index does not expire documents; '{definition.Name}' cannot take ExpireAfter. Time to live is the "
                + "container's DefaultTimeToLive.");
        if (definition.Properties is { Count: > 0 })
            throw new NotSupportedException(
                $"Index '{definition.Name}' carries properties ({string.Join(", ", definition.Properties.Keys)}), which a Cosmos DB "
                + "index cannot express. Use GetIndexingPolicyAsync / SetIndexingPolicyAsync for policy details.");
        var special = definition.Fields.FirstOrDefault(f => f.FieldType != IndexFieldType.Standard);
        if (special != null)
            throw new NotSupportedException(
                $"Index '{definition.Name}' declares field '{special.Name}' as {special.FieldType}, which this manager cannot create "
                + "on Cosmos DB. A spatial index is AddSpatialIndexAsync.");
        if (definition.Fields.Count == 1 && definition.Fields[0].IsDescending)
            throw new NotSupportedException(
                $"Index '{definition.Name}' is a single descending field; a Cosmos DB range index has no direction. Declare it "
                + "ascending, or as part of a composite.");
    }

    private static RegisteredIndex ToEntry(IndexDefinition definition)
    {
        if (definition.Fields.Count == 1)
        {
            return new RegisteredIndex(KindIncluded, new[] { NormalizeIncludedPath(definition.Fields[0].Name) }, Array.Empty<string>());
        }
        return new RegisteredIndex(
            KindComposite,
            definition.Fields.Select(f => NormalizeFieldPath(f.Name)).ToList(),
            definition.Fields.Select(f => (f.IsDescending ? CompositePathSortOrder.Descending : CompositePathSortOrder.Ascending).ToString()).ToList());
    }

    private static IReadOnlyList<IndexField> ToFields(RegisteredIndex entry)
        => entry.Kind == KindIncluded
            ? new[] { new IndexField { Name = TrimPath(entry.Paths[0]) } }
            : entry.Paths.Select((p, i) => new IndexField
            {
                Name = TrimPath(p),
                IsDescending = entry.Orders.ElementAtOrDefault(i) == nameof(CompositePathSortOrder.Descending)
            }).ToList();

    private static string TrimPath(string path)
    {
        var p = path.EndsWith("/?", StringComparison.Ordinal) ? path[..^2] : path;
        return p.TrimStart('/');
    }

    private static bool SameIndex(RegisteredIndex a, RegisteredIndex b)
        => a.Kind == b.Kind && a.Paths.SequenceEqual(b.Paths) && a.Orders.SequenceEqual(b.Orders);

    private static string CompositeKey(IEnumerable<string> paths, IEnumerable<string> orders)
        => string.Join("|", paths.Zip(orders, (p, o) => p + " " + o));

    private static string CompositeKey(IEnumerable<CompositePath> composite)
        => CompositeKey(composite.Select(c => c.Path), composite.Select(c => c.Order.ToString()));

    private static bool IsInPolicy(RegisteredIndex entry, IndexingPolicy policy)
    {
        if (entry.Kind == KindIncluded)
            return policy.IncludedPaths.Any(p => p.Path == entry.Paths[0]);
        var key = CompositeKey(entry.Paths, entry.Orders);
        return policy.CompositeIndexes.Any(c => CompositeKey(c) == key);
    }

    private static void AddToPolicy(RegisteredIndex entry, IndexingPolicy policy)
    {
        if (entry.Kind == KindIncluded)
        {
            var path = entry.Paths[0];
            // A path an earlier drop excluded would contradict its own inclusion.
            foreach (var excluded in policy.ExcludedPaths.Where(e => e.Path == path).ToList())
                policy.ExcludedPaths.Remove(excluded);
            if (!policy.IncludedPaths.Any(p => p.Path == path))
                policy.IncludedPaths.Add(new IncludedPath { Path = path });
            return;
        }

        if (IsInPolicy(entry, policy)) return;
        var composite = new System.Collections.ObjectModel.Collection<CompositePath>();
        for (var i = 0; i < entry.Paths.Count; i++)
        {
            composite.Add(new CompositePath
            {
                Path = entry.Paths[i],
                Order = Enum.Parse<CompositePathSortOrder>(entry.Orders[i])
            });
        }
        policy.CompositeIndexes.Add(composite);
    }

    /// <summary>Removes exactly the entry's paths. Returns whether the policy changed.</summary>
    private static bool RemoveFromPolicy(RegisteredIndex entry, IndexingPolicy policy)
    {
        if (entry.Kind == KindIncluded)
            return RemoveIncludedIndex(policy, entry.Paths[0]) != null;

        var key = CompositeKey(entry.Paths, entry.Orders);
        var match = policy.CompositeIndexes.FirstOrDefault(c => CompositeKey(c) == key);
        return match != null && policy.CompositeIndexes.Remove(match);
    }

    #endregion

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
    /// single-field included path or a composite-index element. Answers a name nothing registered.
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
