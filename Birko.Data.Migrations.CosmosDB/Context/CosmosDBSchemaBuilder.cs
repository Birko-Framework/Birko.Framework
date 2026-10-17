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

    /// <summary>
    /// Drops the index through <see cref="Birko.Data.CosmosDB.IndexManagement.CosmosDBIndexManager"/>, the door
    /// <see cref="CreateIndex"/>'s <c>Build()</c> creates it through, so a migration's index is dropped by its name.
    /// </summary>
    /// <remarks>
    /// TASK-655: this was a no-op, so a migration that dropped an index was recorded as applied with the index still in
    /// the indexing policy (measured).
    /// </remarks>
    public void DropIndex(string collectionName, string indexName)
        => new Birko.Data.CosmosDB.IndexManagement.CosmosDBIndexManager(_database)
            .DropAsync(indexName, collectionName).GetAwaiter().GetResult();

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

    /// <summary>
    /// Removes the field from every document that has it, with a point patch per document in its own partition, as
    /// <see cref="RenameField"/> does, and removes the included path <see cref="AddField"/> adds for it.
    /// </summary>
    /// <remarks>
    /// TASK-655: this only excluded the path from indexing, so every document kept the field and the migration was
    /// recorded as having dropped it (measured). Excluding the path is not a drop, and it left a policy that a later
    /// <see cref="AddField"/> of the same name would contradict, so the indexing policy now keeps no entry for the field.
    /// </remarks>
    public void DropField(string collectionName, string fieldName)
    {
        var container = _database.GetContainer(collectionName);
        var properties = container.ReadContainerAsync().GetAwaiter().GetResult().Resource;
        var pkProperty = (properties.PartitionKeyPath ?? "/id").TrimStart('/').Split('/')[0];
        if (string.IsNullOrEmpty(pkProperty)) pkProperty = "id";

        var fieldPath = CosmosDBDataMigrator.QuoteFieldPath(fieldName);
        var projection = new List<string> { "c.id" };
        if (pkProperty != "id") projection.Add($"c.{pkProperty}");
        var query = $"SELECT {string.Join(", ", projection)} FROM c WHERE IS_DEFINED({fieldPath})";
        var pointer = "/" + fieldName.Replace("~", "~0").Replace("/", "~1");
        foreach (var item in QueryDocuments(container, query))
        {
            string? id = item.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
            if (id == null) continue;

            var pk = pkProperty == "id" ? new PartitionKey(id) : CosmosDBDataMigrator.BuildPartitionKey(item, pkProperty);
            try
            {
                container.PatchItemAsync<dynamic>(id, pk, new[] { PatchOperation.Remove(pointer) }).GetAwaiter().GetResult();
            }
            catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.BadRequest || ex.StatusCode == HttpStatusCode.NotFound)
            {
                // As in RenameField (CR-M103): the field or the document went away since the query; nothing to remove.
            }
        }

        var path = $"/{fieldName}/?";
        var removed = RemoveWhere(properties.IndexingPolicy.IncludedPaths, p => p.Path == path)
                    + RemoveWhere(properties.IndexingPolicy.ExcludedPaths, p => p.Path == path);
        if (removed > 0)
        {
            container.ReplaceContainerAsync(properties).GetAwaiter().GetResult();
        }
    }

    /// <summary>
    /// The documents a query returns, read as a stream and parsed with System.Text.Json, so the result does not depend on
    /// the serializer of the client the caller built the <see cref="Database"/> with. Measured on TASK-655: through the
    /// SDK's default (Newtonsoft) serializer a <see cref="JsonElement"/> result comes back empty, and reading it throws.
    /// </summary>
    private static IEnumerable<JsonElement> QueryDocuments(Container container, string query)
    {
        using var iterator = container.GetItemQueryStreamIterator(new QueryDefinition(query));
        while (iterator.HasMoreResults)
        {
            using var response = iterator.ReadNextAsync().GetAwaiter().GetResult();
            response.EnsureSuccessStatusCode();
            using var page = JsonDocument.Parse(response.Content);
            foreach (var document in page.RootElement.GetProperty("Documents").EnumerateArray())
            {
                yield return document.Clone();
            }
        }
    }

    private static int RemoveWhere<TPath>(System.Collections.ObjectModel.Collection<TPath> paths, Func<TPath, bool> match)
    {
        var doomed = paths.Where(match).ToList();
        foreach (var p in doomed) paths.Remove(p);
        return doomed.Count;
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
