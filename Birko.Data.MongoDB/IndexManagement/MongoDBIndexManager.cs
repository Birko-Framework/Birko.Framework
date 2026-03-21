using Birko.Data.Patterns.IndexManagement;
using MongoDB.Bson;
using MongoDB.Driver;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Birko.Data.MongoDB.IndexManagement
{
    /// <summary>
    /// MongoDB implementation of <see cref="IIndexManager"/>.
    /// Scope = collection name (required).
    /// </summary>
    public class MongoDBIndexManager : IIndexManager
    {
        private readonly MongoDBClient _client;

        public MongoDBIndexManager(MongoDBClient client)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
        }

        /// <inheritdoc />
        public async Task<bool> ExistsAsync(string indexName, string? scope = null, CancellationToken ct = default)
        {
            ValidateScope(scope);
            var collection = _client.Database.GetCollection<BsonDocument>(scope!);

            using var cursor = await collection.Indexes.ListAsync(ct).ConfigureAwait(false);
            var indexes = await cursor.ToListAsync(ct).ConfigureAwait(false);
            return indexes.Any(idx => idx["name"].AsString == indexName);
        }

        /// <inheritdoc />
        public async Task CreateAsync(IndexDefinition definition, string? scope = null, CancellationToken ct = default)
        {
            ValidateScope(scope);
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            if (string.IsNullOrWhiteSpace(definition.Name)) throw new ArgumentException("Index name is required.", nameof(definition));
            if (definition.Fields == null || definition.Fields.Count == 0) throw new ArgumentException("At least one field is required.", nameof(definition));

            var collection = _client.Database.GetCollection<BsonDocument>(scope!);

            var keysBuilder = Builders<BsonDocument>.IndexKeys;
            var keysList = new List<IndexKeysDefinition<BsonDocument>>();

            foreach (var field in definition.Fields)
            {
                keysList.Add(field.FieldType switch
                {
                    IndexFieldType.Text => keysBuilder.Text(field.Name),
                    IndexFieldType.Hashed => keysBuilder.Hashed(field.Name),
                    IndexFieldType.Geo2d => keysBuilder.Geo2D(field.Name),
                    IndexFieldType.Geo2dSphere => keysBuilder.Geo2DSphere(field.Name),
                    _ => field.IsDescending
                        ? keysBuilder.Descending(field.Name)
                        : keysBuilder.Ascending(field.Name)
                });
            }

            var keys = keysBuilder.Combine(keysList);
            var options = new CreateIndexOptions
            {
                Name = definition.Name,
                Background = true,
                Unique = definition.Unique,
                Sparse = definition.Sparse
            };

            if (definition.ExpireAfter.HasValue)
            {
                options.ExpireAfter = definition.ExpireAfter.Value;
            }

            try
            {
                await collection.Indexes.CreateOneAsync(
                    new CreateIndexModel<BsonDocument>(keys, options), cancellationToken: ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                throw new IndexManagementException(
                    $"Failed to create index '{definition.Name}' on collection '{scope}'.",
                    definition.Name, scope, ex);
            }
        }

        /// <inheritdoc />
        public async Task DropAsync(string indexName, string? scope = null, CancellationToken ct = default)
        {
            ValidateScope(scope);
            if (string.IsNullOrWhiteSpace(indexName)) throw new ArgumentException("Index name is required.", nameof(indexName));

            var collection = _client.Database.GetCollection<BsonDocument>(scope!);

            try
            {
                await collection.Indexes.DropOneAsync(indexName, ct).ConfigureAwait(false);
            }
            catch (MongoCommandException ex) when (ex.CodeName == "IndexNotFound")
            {
                // Idempotent: treat "not found" as success
            }
            catch (Exception ex)
            {
                throw new IndexManagementException(
                    $"Failed to drop index '{indexName}' on collection '{scope}'.",
                    indexName, scope, ex);
            }
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<Patterns.IndexManagement.IndexInfo>> ListAsync(string? scope = null, CancellationToken ct = default)
        {
            ValidateScope(scope);
            var collection = _client.Database.GetCollection<BsonDocument>(scope!);

            using var cursor = await collection.Indexes.ListAsync(ct).ConfigureAwait(false);
            var indexes = await cursor.ToListAsync(ct).ConfigureAwait(false);

            var result = new List<Patterns.IndexManagement.IndexInfo>();
            foreach (var idx in indexes)
            {
                result.Add(ParseIndexDocument(idx));
            }
            return result;
        }

        /// <inheritdoc />
        public async Task<Patterns.IndexManagement.IndexInfo?> GetInfoAsync(string indexName, string? scope = null, CancellationToken ct = default)
        {
            ValidateScope(scope);
            if (string.IsNullOrWhiteSpace(indexName)) throw new ArgumentException("Index name is required.", nameof(indexName));

            var collection = _client.Database.GetCollection<BsonDocument>(scope!);

            using var cursor = await collection.Indexes.ListAsync(ct).ConfigureAwait(false);
            var indexes = await cursor.ToListAsync(ct).ConfigureAwait(false);

            var idx = indexes.FirstOrDefault(i => i["name"].AsString == indexName);
            if (idx == null) return null;

            // Get index size from collStats if available
            var info = ParseIndexDocument(idx);
            try
            {
                var stats = await _client.Database.RunCommandAsync<BsonDocument>(
                    new BsonDocument("collStats", scope!), cancellationToken: ct).ConfigureAwait(false);

                if (stats.Contains("indexSizes") && stats["indexSizes"].AsBsonDocument.Contains(indexName))
                {
                    info.SizeInBytes = stats["indexSizes"][indexName].ToInt64();
                }
            }
            catch
            {
                // collStats may fail on some configurations; size stays -1
            }

            return info;
        }

        #region MongoDB-specific extensions

        /// <summary>
        /// Creates a compound index from multiple field definitions.
        /// Convenience method — equivalent to CreateAsync with multiple fields.
        /// </summary>
        public Task CreateCompoundIndexAsync(string collectionName, string indexName, IEnumerable<IndexField> fields, bool unique = false, CancellationToken ct = default)
        {
            return CreateAsync(new IndexDefinition
            {
                Name = indexName,
                Fields = fields.ToList(),
                Unique = unique
            }, collectionName, ct);
        }

        /// <summary>
        /// Creates a TTL index on a date field.
        /// </summary>
        public Task CreateTtlIndexAsync(string collectionName, string fieldName, TimeSpan expireAfter, CancellationToken ct = default)
        {
            return CreateAsync(new IndexDefinition
            {
                Name = $"ttl_{fieldName}",
                Fields = new[] { IndexField.Ascending(fieldName) },
                ExpireAfter = expireAfter
            }, collectionName, ct);
        }

        /// <summary>
        /// Creates a text index for full-text search on the specified fields.
        /// </summary>
        public Task CreateTextIndexAsync(string collectionName, string indexName, IEnumerable<string> fieldNames, CancellationToken ct = default)
        {
            return CreateAsync(new IndexDefinition
            {
                Name = indexName,
                Fields = fieldNames.Select(f => IndexField.Text(f)).ToList()
            }, collectionName, ct);
        }

        /// <summary>
        /// Drops all non-default indexes on a collection.
        /// The _id_ index cannot be dropped.
        /// </summary>
        public async Task DropAllAsync(string collectionName, CancellationToken ct = default)
        {
            ValidateScope(collectionName);
            var collection = _client.Database.GetCollection<BsonDocument>(collectionName);
            await collection.Indexes.DropAllAsync(ct).ConfigureAwait(false);
        }

        #endregion

        #region Helpers

        private static void ValidateScope(string? scope)
        {
            if (string.IsNullOrWhiteSpace(scope))
                throw new ArgumentException("Collection name (scope) is required for MongoDB index management.", nameof(scope));
        }

        private static Patterns.IndexManagement.IndexInfo ParseIndexDocument(BsonDocument idx)
        {
            var info = new Patterns.IndexManagement.IndexInfo
            {
                Name = idx["name"].AsString,
                Unique = idx.Contains("unique") && idx["unique"].AsBoolean,
                Sparse = idx.Contains("sparse") && idx["sparse"].AsBoolean,
                State = "ready"
            };

            if (idx.Contains("expireAfterSeconds"))
            {
                info.ExpireAfter = TimeSpan.FromSeconds(idx["expireAfterSeconds"].ToInt64());
            }

            // Parse key fields
            if (idx.Contains("key"))
            {
                var fields = new List<IndexField>();
                foreach (var element in idx["key"].AsBsonDocument)
                {
                    var field = new IndexField { Name = element.Name };

                    if (element.Value.IsInt32 || element.Value.IsInt64 || element.Value.IsDouble)
                    {
                        var val = element.Value.ToInt32();
                        field.IsDescending = val < 0;
                        field.FieldType = IndexFieldType.Standard;
                    }
                    else if (element.Value.IsString)
                    {
                        field.FieldType = element.Value.AsString switch
                        {
                            "text" => IndexFieldType.Text,
                            "hashed" => IndexFieldType.Hashed,
                            "2d" => IndexFieldType.Geo2d,
                            "2dsphere" => IndexFieldType.Geo2dSphere,
                            _ => IndexFieldType.Standard
                        };
                    }

                    fields.Add(field);
                }
                info.Fields = fields;
            }

            return info;
        }

        #endregion
    }
}
