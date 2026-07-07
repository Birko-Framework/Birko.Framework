using System;
using System.Collections.Generic;
using System.Text.Json;
using Birko.Data.Patterns.IndexManagement;
using Birko.Data.Patterns.Schema;
using Microsoft.Azure.Cosmos;

namespace Birko.Data.Migrations.CosmosDB.Context;

public class CosmosDBSchemaBuilder : ISchemaBuilder
{
    private readonly Database _database;

    public CosmosDBSchemaBuilder(Database database)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
    }

    public ICollectionBuilder CreateCollection(string name)
    {
        var properties = new ContainerProperties(name, "/id");
        _database.CreateContainerIfNotExistsAsync(properties).GetAwaiter().GetResult();
        return new CosmosCollectionBuilder(name, _database);
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
        return new CosmosIndexBuilder(collectionName, indexName, _database);
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

        var projection = new List<string> { "c.id", $"c[\"{oldName}\"] AS oldValue" };
        if (pkProperty != "id") projection.Add($"c.{pkProperty}");
        var query = $"SELECT {string.Join(", ", projection)} FROM c WHERE IS_DEFINED(c[\"{oldName}\"])";
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
                catch
                {
                    // Skip documents that don't have the old field / fail to patch.
                }
            }
        }
    }

    private class CosmosCollectionBuilder : ICollectionBuilder
    {
        private readonly string _name;
        private readonly Database _database;
        private string _partitionKeyPath = "/id";

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
        {
            // Cosmos DB is schema-less — field definitions are not needed for containers.
            // However, if a field is marked as primary, use it as partition key.
            if (isPrimary)
            {
                _partitionKeyPath = $"/{name}";
            }
            return this;
        }

        public ICollectionBuilder WithField(FieldDescriptor field)
        {
            if (field.IsPrimary)
            {
                _partitionKeyPath = $"/{field.Name}";
            }
            return this;
        }
    }

    private class CosmosIndexBuilder : IIndexBuilder
    {
        private readonly string _collectionName;
        private readonly string _indexName;
        private readonly Database _database;
        private readonly List<(string Name, bool Descending)> _fields = new();
        private bool _unique;

        public CosmosIndexBuilder(string collectionName, string indexName, Database database)
        {
            _collectionName = collectionName;
            _indexName = indexName;
            _database = database;
        }

        public IIndexBuilder WithField(string name, bool descending = false, IndexFieldType fieldType = IndexFieldType.Standard)
        {
            _fields.Add((name, descending));
            return this;
        }

        public IIndexBuilder Unique()
        {
            _unique = true;
            return this;
        }

        public IIndexBuilder Sparse() => this;

        public IIndexBuilder WithProperty(string key, object value) => this;

        /// <summary>
        /// Exposes whether <see cref="Unique"/> was called. Cosmos DB expresses uniqueness via
        /// unique-key policies declared at container creation time, not through index metadata,
        /// so <see cref="_unique"/> is captured here but not yet translated into a container-level
        /// policy. Reserved for when that wiring lands.
        /// </summary>
        internal bool IsUnique => _unique;
    }
}
