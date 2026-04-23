using System;
using System.Collections.Generic;
using System.Linq;
using Birko.Data.Migrations.Context;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Birko.Data.Migrations.MongoDB.Context
{
    public class MongoDataMigrator : IDataMigrator
    {
        private readonly IMongoDatabase _database;

        public MongoDataMigrator(IMongoDatabase database)
        {
            _database = database;
        }

        public void UpdateDocuments(string collection, string filterJson, IDictionary<string, object> updates)
        {
            if (updates == null || updates.Count == 0) return;

            var coll = _database.GetCollection<BsonDocument>(collection);
            var filter = ParseFilter(filterJson);
            var updateFields = updates.Select(kvp => new BsonElement(kvp.Key, BsonValue.Create(kvp.Value)));
            var update = new BsonDocument("$set", new BsonDocument(updateFields));
            coll.UpdateMany(filter, update);
        }

        public void DeleteDocuments(string collection, string filterJson)
        {
            var coll = _database.GetCollection<BsonDocument>(collection);
            var filter = ParseFilter(filterJson);
            coll.DeleteMany(filter);
        }

        public long CountDocuments(string collection, string? filterJson = null)
        {
            var coll = _database.GetCollection<BsonDocument>(collection);
            var filter = ParseFilter(filterJson);
            return coll.CountDocuments(filter);
        }

        public void CopyData(string sourceCollection, string targetCollection, string? transformJson = null)
        {
            var source = _database.GetCollection<BsonDocument>(sourceCollection);
            var target = _database.GetCollection<BsonDocument>(targetCollection);

            // Use $merge for server-side copy
            var pipeline = new[]
            {
                new BsonDocument("$merge", new BsonDocument("into", targetCollection))
            };
            source.Aggregate<BsonDocument>(pipeline).ToList();
        }

        public void BulkInsert(string collection, IEnumerable<IDictionary<string, object>> documents)
        {
            if (documents == null) return;

            var coll = _database.GetCollection<BsonDocument>(collection);
            var bsonDocs = documents
                .Where(d => d != null && d.Count > 0)
                .Select(d => new BsonDocument(d.Select(kvp => new BsonElement(kvp.Key, BsonValue.Create(kvp.Value)))))
                .ToList();

            if (bsonDocs.Count > 0)
                coll.InsertMany(bsonDocs);
        }

        private static FilterDefinition<BsonDocument> ParseFilter(string? filterJson)
        {
            if (string.IsNullOrWhiteSpace(filterJson) || filterJson!.Trim() == "{}")
                return Builders<BsonDocument>.Filter.Empty;

            var doc = BsonDocument.Parse(filterJson);
            return new BsonDocumentFilterDefinition<BsonDocument>(doc);
        }
    }
}
