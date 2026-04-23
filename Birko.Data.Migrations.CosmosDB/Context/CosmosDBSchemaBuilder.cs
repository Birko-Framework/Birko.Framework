using System;
using System.Collections.Generic;
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
        // Cosmos DB does not have a native rename. Use a query to update documents.
        var container = _database.GetContainer(collectionName);

        // For bulk rename, use a query-based approach
        var query = $"SELECT c.id FROM c";
        var iterator = container.GetItemQueryIterator<dynamic>(new QueryDefinition(query));

        while (iterator.HasMoreResults)
        {
            var response = iterator.ReadNextAsync().GetAwaiter().GetResult();
            foreach (var item in response)
            {
                string? id = item.id?.ToString();
                if (id != null)
                {
                    try
                    {
                        container.PatchItemAsync<dynamic>(
                            id,
                            new PartitionKey(id),
                            new[]
                            {
                                PatchOperation.Set($"/{newName}", $"[RESTRICT SCHEMA] renamed from {oldName}"),
                                PatchOperation.Remove($"/{oldName}")
                            }
                        ).GetAwaiter().GetResult();
                    }
                    catch
                    {
                        // Skip documents that don't have the old field
                    }
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
    }
}
