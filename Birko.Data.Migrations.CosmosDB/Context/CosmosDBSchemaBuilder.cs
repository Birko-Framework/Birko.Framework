using System;
using System.Collections.Generic;
using System.Net;
using System.Text.Json;
using Birko.Data.Patterns.IndexManagement;
using Birko.Data.Patterns.Schema;
using Microsoft.Azure.Cosmos;

namespace Birko.Data.Migrations.CosmosDB.Context;

public class CosmosDBSchemaBuilder : ISchemaBuilder
{
    // TASK-627: the chains handed out, so the runner can fail a migration that never called Build().
    private readonly SchemaBuildLedger _ledger = new();

    /// <inheritdoc />
    public System.Collections.Generic.IReadOnlyList<string> PendingBuilds => _ledger.Pending;

    private readonly Database _database;

    public CosmosDBSchemaBuilder(Database database)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
    }

    public ICollectionBuilder CreateCollection(string name)
    {
        // SH-M174 (TASK-152): the container is created by Build(), once the fields are declared. It was created here,
        // partitioned on /id, before WithField(..., isPrimary: true) could say otherwise, and a partition key cannot
        // change afterwards; the builder recorded the declared one and nothing read it.
        return _ledger.Track(new CosmosCollectionBuilder(name, _database), name);
    }

    public void DropCollection(string name)
    {
        var container = _database.GetContainer(name);
        container.DeleteContainerAsync().GetAwaiter().GetResult();
    }

    public bool CollectionExists(string name)
    {
        try
        {
            var container = _database.GetContainer(name);
            container.ReadContainerAsync().GetAwaiter().GetResult();
            return true;
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return false;
        }
    }

    public IIndexBuilder CreateIndex(string collectionName, string indexName)
    {
        return _ledger.Track(new CosmosIndexBuilder(collectionName, indexName, _database), collectionName, indexName);
    }

    public void DropIndex(string collectionName, string indexName)
    {
        // Cosmos DB indexes are managed through indexing policy, not individual index objects.
        // Use Raw() to modify the indexing policy directly if needed.
    }

    public void AddField(string collectionName, FieldDescriptor field)
    {
        // Cosmos DB is schema-less by default. Adding a field means updating the indexing policy
        // to include the new path if custom indexing is desired.
        var container = _database.GetContainer(collectionName);
        var response = container.ReadContainerAsync().GetAwaiter().GetResult();
        var properties = response.Resource;

        var path = $"/{field.Name}/?";
        if (!properties.IndexingPolicy.IncludedPaths.Any(p => p.Path == path))
        {
            properties.IndexingPolicy.IncludedPaths.Add(new IncludedPath { Path = path });
            container.ReplaceContainerAsync(properties).GetAwaiter().GetResult();
        }
    }

    public void DropField(string collectionName, string fieldName)
    {
        // Cosmos DB does not support removing fields from existing documents.
        // Optionally exclude the path from indexing.
        var container = _database.GetContainer(collectionName);
        var response = container.ReadContainerAsync().GetAwaiter().GetResult();
        var properties = response.Resource;

        var path = $"/{fieldName}/?";
        if (!properties.IndexingPolicy.ExcludedPaths.Any(p => p.Path == path))
        {
            properties.IndexingPolicy.ExcludedPaths.Add(new ExcludedPath { Path = path });
            container.ReplaceContainerAsync(properties).GetAwaiter().GetResult();
        }
    }

    public void RenameField(string collectionName, string oldName, string newName)
    {
        // Cosmos DB has no native rename, so copy each document's OLD value to the new field and
        // remove the old field. The query reads the actual old value (and the partition-key field)
        // so the value is preserved (CR-H056: it used to write a debug placeholder — data loss) and
        // the point patch targets the correct partition, not the id (CR-H055).
        var container = _database.GetContainer(collectionName);
        var pkPath = container.ReadContainerAsync().GetAwaiter().GetResult().Resource.PartitionKeyPath ?? "/id";
        var pkProperty = pkPath.TrimStart('/').Split('/')[0];
        if (string.IsNullOrEmpty(pkProperty)) pkProperty = "id";

        // TASK-450: the field name reached the statement with NO escaping, while the sibling
        // CosmosDBDataMigrator escaped its identifiers (CR-M104) -- one rule, two behaviours. Both now
        // share CosmosDBDataMigrator.QuoteFieldPath. An identifier cannot be parameterised, so escaping
        // is the containment here, unlike the compared VALUES in that file.
        var oldPath = CosmosDBDataMigrator.QuoteFieldPath(oldName);
        var projection = new List<string> { "c.id", $"{oldPath} AS oldValue" };
        if (pkProperty != "id") projection.Add($"c.{pkProperty}");
        var query = $"SELECT {string.Join(", ", projection)} FROM c WHERE IS_DEFINED({oldPath})";
        var iterator = container.GetItemQueryIterator<JsonElement>(new QueryDefinition(query));

        while (iterator.HasMoreResults)
        {
            var response = iterator.ReadNextAsync().GetAwaiter().GetResult();
            foreach (var item in response)
            {
                string? id = item.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
                if (id == null) continue;

                var oldValue = item.TryGetProperty("oldValue", out var v) ? v : default;
                var pk = pkProperty == "id"
                    ? new PartitionKey(id)
                    : CosmosDBDataMigrator.BuildPartitionKey(item, pkProperty);
                try
                {
                    container.PatchItemAsync<dynamic>(
                        id,
                        pk,
                        new[]
                        {
                            PatchOperation.Set($"/{newName}", oldValue),
                            PatchOperation.Remove($"/{oldName}")
                        }
                    ).GetAwaiter().GetResult();
                }
                catch (CosmosException ex) when (
                    ex.StatusCode == HttpStatusCode.BadRequest || ex.StatusCode == HttpStatusCode.NotFound)
                {
                    // CR-M103: only swallow the "field/document absent" cases (removing a missing path
                    // yields BadRequest; a missing document yields NotFound) so the rename stays
                    // idempotent. Throttling (429), auth (401/403) and service errors (503) now
                    // propagate instead of silently corrupting a subset of documents.
                }
            }
        }
    }

    private class CosmosCollectionBuilder : ICollectionBuilder
    {
        private readonly string _name;
        private readonly Database _database;

        public CosmosCollectionBuilder(string name, Database database)
        {
            _name = name;
            _database = database;
        }

        public ICollectionBuilder WithField(string name, FieldType type,
            bool isPrimary = false, bool isUnique = false,
            bool isRequired = false, int? maxLength = null,
            int? precision = null, int? scale = null,
            bool isAutoIncrement = false, object? defaultValue = null)
            => WithField(new FieldDescriptor(name) { Type = type, IsPrimary = isPrimary, IsUnique = isUnique });

        /// <summary>
        /// Cosmos DB is schema-less, so a field declares nothing but its key role. The container is partitioned on
        /// <c>/id</c>, the only key the Birko stores address (TASK-614); a primary field that is not the document id is
        /// refused rather than recorded and ignored (SH-M174), and so is a unique field (a unique key holds per logical
        /// partition, so on <c>/id</c> it rejects nothing, measured at TASK-628).
        /// </summary>
        public ICollectionBuilder WithField(FieldDescriptor field)
        {
            if (field.IsPrimary && !IsDocumentId(field.Name))
                throw new NotSupportedException(
                    $"Collection '{_name}': field '{field.Name}' is declared primary, and a Cosmos DB container for the Birko stores "
                    + "is partitioned on /id (the document id). Declare the id field primary, or none.");
            if (field.IsUnique && !field.IsPrimary)
                throw new NotSupportedException(
                    $"Collection '{_name}': field '{field.Name}' is declared unique, which Cosmos DB cannot enforce on a container "
                    + "partitioned on /id (a unique key holds within one logical partition).");
            return this;
        }

        private static bool IsDocumentId(string name)
            => string.Equals(name, "id", StringComparison.OrdinalIgnoreCase) || string.Equals(name, "Guid", StringComparison.OrdinalIgnoreCase);

        public void Build()
            => _database.CreateContainerIfNotExistsAsync(new ContainerProperties(_name, "/id")).GetAwaiter().GetResult();
    }

    private class CosmosIndexBuilder : IIndexBuilder
    {
        private readonly string _collectionName;
        private readonly string _indexName;
        private readonly Database _database;
        private readonly List<(string Name, bool Descending, IndexFieldType FieldType)> _fields = new();
        private bool _unique;

        public CosmosIndexBuilder(string collectionName, string indexName, Database database)
        {
            _collectionName = collectionName;
            _indexName = indexName;
            _database = database;
        }

        public IIndexBuilder WithField(string name, bool descending = false, IndexFieldType fieldType = IndexFieldType.Standard)
        {
            _fields.Add((name, descending, fieldType));
            return this;
        }

        public IIndexBuilder Unique()
        {
            _unique = true;
            return this;
        }

        /// <summary>
        /// Refused, with everything else this builder accumulates (TASK-274). See <see cref="Build"/>.
        /// </summary>
        public IIndexBuilder Sparse()
            => throw Birko.Data.Patterns.Schema.IndexBuilderSupport.Unsupported(
                "CosmosDB", "a sparse index", "Cosmos DB has no individual index objects — indexing is a container-level policy", "Use Raw() to modify the container indexing policy.");

        /// <inheritdoc cref="Sparse"/>
        public IIndexBuilder WithProperty(string key, object value)
            => throw Birko.Data.Patterns.Schema.IndexBuilderSupport.Unsupported(
                "CosmosDB", $"index property '{key}'", "Cosmos DB has no individual index objects — indexing is a container-level policy", "Use Raw() to modify the container indexing policy.");

        /// <summary>
        /// Creates the index through <see cref="Birko.Data.CosmosDB.IndexManagement.CosmosDBIndexManager"/>, the other
        /// door onto the same feature (SH-M200, TASK-152). TASK-274 refused the whole declaration because the builder
        /// created nothing; since TASK-628 the manager keeps the index's name and honours or refuses each flag
        /// (<c>Unique</c> is refused there: it would reject nothing on /id), so a migration's index is the manager's.
        /// </summary>
        public void Build()
        {
            if (_fields.Count == 0)
                throw new InvalidOperationException($"Index '{_indexName}' on '{_collectionName}' declares no field.");

            var definition = new IndexDefinition
            {
                Name = _indexName,
                Unique = _unique,
                Fields = _fields.ConvertAll(f => new IndexField { Name = f.Name, IsDescending = f.Descending, FieldType = f.FieldType }),
            };
            new Birko.Data.CosmosDB.IndexManagement.CosmosDBIndexManager(_database)
                .CreateAsync(definition, _collectionName).GetAwaiter().GetResult();
        }
    }
}
