using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Birko.Data.Localization.Models;
using Birko.Data.Models;
using Birko.Data.MongoDB.IndexManagement;
using Birko.Data.Patterns.IndexManagement;

namespace Birko.Data.Localization.MongoDB;

/// <summary>
/// The indexes a MongoDB translation collection needs (TASK-699), as <c>EntityTranslationMapping</c> declares them on SQL
/// (TASK-680/681): a unique key over <c>(EntityGuid, EntityType, FieldName, Culture)</c>, and an index over
/// <c>ValueHash</c>, which value lookups select rows by.
/// </summary>
/// <remarks>
/// Without them a lookup by value scans the collection, and only the key Guid (TASK-698) refuses a duplicate key. With
/// the unique index, MongoDB also refuses a duplicate stored under any other Guid: a row written around the upsert, or
/// one stored before TASK-698 with a random Guid. The stores already translate MongoDB's duplicate-key error (E11000)
/// into <c>StoreUniqueViolationException</c>, so the upsert's retry takes it as it takes a duplicate Guid.
/// </remarks>
public static class LocalizationMongoIndexes
{
    /// <summary>The unique key's name on <paramref name="collection"/>.</summary>
    public static string KeyIndexName(string collection) => $"UX_{collection}_Key";

    /// <summary>The value index's name on <paramref name="collection"/>.</summary>
    public static string ValueHashIndexName(string collection) => $"IX_{collection}_ValueHash";

    /// <summary>The two indexes, for a collection named <paramref name="collection"/>.</summary>
    public static IReadOnlyList<IndexDefinition> Definitions(string collection)
    {
        ArgumentException.ThrowIfNullOrEmpty(collection);
        return new[]
        {
            new IndexDefinition
            {
                Name = KeyIndexName(collection),
                Unique = true,
                Fields = new[]
                {
                    IndexField.Ascending(nameof(IEntityTranslation.EntityGuid)),
                    IndexField.Ascending(nameof(IEntityTranslation.EntityType)),
                    IndexField.Ascending(nameof(IEntityTranslation.FieldName)),
                    IndexField.Ascending(nameof(IEntityTranslation.Culture)),
                },
            },
            new IndexDefinition
            {
                Name = ValueHashIndexName(collection),
                Fields = new[] { IndexField.Ascending(nameof(IEntityTranslation.ValueHash)) },
            },
        };
    }

    /// <summary>
    /// Creates both indexes on the store's collection, each only if it is not there. Idempotent; call it once at
    /// startup, after <c>SetSettings</c>.
    /// </summary>
    /// <exception cref="InvalidOperationException">The store has no client: <c>SetSettings</c> was not called.</exception>
    /// <exception cref="IndexManagementException">
    /// MongoDB refused an index: the unique key fails to build over a collection that already holds a duplicate key
    /// (E11000). Remove the duplicates, keeping the newest row per key, then call this again.
    /// </exception>
    public static async Task EnsureAsync<TTranslation>(Birko.Data.MongoDB.Stores.AsyncMongoDBStore<TTranslation> store, CancellationToken ct = default)
        where TTranslation : AbstractModel, IEntityTranslation
    {
        ArgumentNullException.ThrowIfNull(store);
        await store.InitAsync(ct).ConfigureAwait(false);
        await EnsureOnAsync(store.Client, typeof(TTranslation).Name, ct).ConfigureAwait(false);
    }

    // The stores name a collection after the model type (MongoDBClient.GetCollection<T>()).
    private static async Task EnsureOnAsync(Birko.Data.MongoDB.MongoDBClient? client, string collection, CancellationToken ct)
    {
        if (client == null)
        {
            throw new InvalidOperationException(
                $"The translation store for '{collection}' has no MongoDB client: call SetSettings(...) before ensuring its indexes.");
        }
        var manager = new MongoDBIndexManager(client);
        foreach (var index in Definitions(collection))
        {
            if (!await manager.ExistsAsync(index.Name, collection, ct).ConfigureAwait(false))
            {
                await manager.CreateAsync(index, collection, ct).ConfigureAwait(false);
            }
        }
    }
}
