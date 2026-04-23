using System;
using System.Collections.Generic;
using System.Linq;
using Birko.Data.Patterns.IndexManagement;
using Birko.Data.Patterns.Schema;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Birko.Data.Migrations.MongoDB.Context
{
    public class MongoSchemaBuilder : ISchemaBuilder
    {
        private readonly IMongoDatabase _database;

        public MongoSchemaBuilder(IMongoDatabase database)
        {
            _database = database;
        }

        public ICollectionBuilder CreateCollection(string name)
        {
            _database.CreateCollection(name);
            return new MongoCollectionBuilder(name, _database);
        }

        public void DropCollection(string name)
        {
            _database.DropCollection(name);
        }

        public bool CollectionExists(string name)
        {
            var filter = new ListCollectionNamesOptions { Filter = Builders<BsonDocument>.Filter.Eq("name", name) };
            return _database.ListCollectionNames(filter).Any();
        }

        public IIndexBuilder CreateIndex(string collectionName, string indexName)
        {
            return new MongoIndexBuilder(collectionName, indexName, _database);
        }

        public void DropIndex(string collectionName, string indexName)
        {
            _database.GetCollection<BsonDocument>(collectionName).Indexes.DropOne(indexName);
        }

        public void AddField(string collectionName, FieldDescriptor field)
        {
            // MongoDB is schema-less — no-op
        }

        public void DropField(string collectionName, string fieldName)
        {
            // MongoDB is schema-less — could $unset but typically unnecessary
        }

        public void RenameField(string collectionName, string oldName, string newName)
        {
            var collection = _database.GetCollection<BsonDocument>(collectionName);
            var update = Builders<BsonDocument>.Update.Rename(oldName, newName);
            collection.UpdateMany(Builders<BsonDocument>.Filter.Empty, update);
        }

        private class MongoCollectionBuilder : ICollectionBuilder
        {
            private readonly string _name;
            private readonly IMongoDatabase _database;

            public MongoCollectionBuilder(string name, IMongoDatabase database)
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
                return this;
            }

            public ICollectionBuilder WithField(FieldDescriptor field)
            {
                return this;
            }
        }

        private class MongoIndexBuilder : IIndexBuilder
        {
            private readonly string _collectionName;
            private readonly string _indexName;
            private readonly IMongoDatabase _database;
            private readonly List<IndexFieldDefinition> _fields = new();
            private bool _unique;

            public MongoIndexBuilder(string collectionName, string indexName, IMongoDatabase database)
            {
                _collectionName = collectionName;
                _indexName = indexName;
                _database = database;
            }

            public IIndexBuilder WithField(string name, bool descending = false, IndexFieldType fieldType = IndexFieldType.Standard)
            {
                _fields.Add(new IndexFieldDefinition(name, descending, fieldType));
                return this;
            }

            public IIndexBuilder Unique()
            {
                _unique = true;
                return this;
            }

            public IIndexBuilder Sparse() => this;

            public IIndexBuilder WithProperty(string key, object value) => this;

            internal void Build()
            {
                if (_fields.Count == 0) return;

                var keys = Builders<BsonDocument>.IndexKeys.Combine(
                    _fields.Select(f => f.Descending
                        ? Builders<BsonDocument>.IndexKeys.Descending(f.Name)
                        : Builders<BsonDocument>.IndexKeys.Ascending(f.Name)));

                var options = new CreateIndexOptions { Name = _indexName, Unique = _unique };
                var model = new CreateIndexModel<BsonDocument>(keys, options);
                _database.GetCollection<BsonDocument>(_collectionName).Indexes.CreateOne(model);
            }

            private record IndexFieldDefinition(string Name, bool Descending, IndexFieldType Type);
        }
    }
}
