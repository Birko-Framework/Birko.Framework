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
        private readonly IClientSessionHandle? _session;

        // The session must be threaded to every operation for it to participate in the runner's
        // transaction — a driver call issued without the session commits immediately, regardless of
        // any enclosing transaction (CR-C09).
        public MongoDataMigrator(IMongoDatabase database, IClientSessionHandle? session = null)
        {
            _database = database;
            _session = session;
        }

        public void UpdateDocuments(string collection, string filterJson, IDictionary<string, object> updates)
        {
            if (updates == null || updates.Count == 0) return;

            var coll = _database.GetCollection<BsonDocument>(collection);
            var filter = ParseFilter(filterJson);
            var updateFields = updates.Select(kvp => new BsonElement(kvp.Key, BsonValue.Create(kvp.Value)));
            var update = new BsonDocument("$set", new BsonDocument(updateFields));
            if (_session != null) coll.UpdateMany(_session, filter, update);
            else coll.UpdateMany(filter, update);
        }

        public void DeleteDocuments(string collection, string filterJson)
        {
            var coll = _database.GetCollection<BsonDocument>(collection);
            var filter = ParseFilter(filterJson);
            if (_session != null) coll.DeleteMany(_session, filter);
            else coll.DeleteMany(filter);
        }

        public long CountDocuments(string collection, string? filterJson = null)
        {
            var coll = _database.GetCollection<BsonDocument>(collection);
            var filter = ParseFilter(filterJson);
            return _session != null ? coll.CountDocuments(_session, filter) : coll.CountDocuments(filter);
        }

        public void CopyData(string sourceCollection, string targetCollection, string? transformJson = null)
        {
            var source = _database.GetCollection<BsonDocument>(sourceCollection);

            // CR-M112: honor transformJson (a stage or array of stages) instead of silently ignoring it.
            var pipeline = BuildCopyPipeline(targetCollection, transformJson);
            if (_session != null) source.Aggregate<BsonDocument>(_session, pipeline).ToList();
            else source.Aggregate<BsonDocument>(pipeline).ToList();
        }

        /// <summary>
        /// Builds the copy aggregation pipeline: any caller-supplied transform stage(s) followed by the
        /// server-side <c>$merge</c> into the target collection (CR-M112 — the transform used to be
        /// dropped). <paramref name="transformJson"/> may be a single stage object or a JSON array of
        /// stages; null/empty/<c>{}</c> means a straight copy.
        /// </summary>
        internal static BsonDocument[] BuildCopyPipeline(string targetCollection, string? transformJson)
        {
            var stages = new List<BsonDocument>();

            if (!string.IsNullOrWhiteSpace(transformJson) && transformJson!.Trim() != "{}")
            {
                var trimmed = transformJson.Trim();
                if (trimmed.StartsWith("["))
                {
                    // Wrap so BsonDocument.Parse (object-only) can read a top-level array of stages.
                    var wrapped = BsonDocument.Parse($"{{\"stages\":{trimmed}}}");
                    foreach (var stage in wrapped["stages"].AsBsonArray)
                    {
                        stages.Add(stage.AsBsonDocument);
                    }
                }
                else
                {
                    stages.Add(BsonDocument.Parse(trimmed));
                }
            }

            stages.Add(new BsonDocument("$merge", new BsonDocument("into", targetCollection)));
            return stages.ToArray();
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
            {
                if (_session != null) coll.InsertMany(_session, bsonDocs);
                else coll.InsertMany(bsonDocs);
            }
        }

        internal static FilterDefinition<BsonDocument> ParseFilter(string? filterJson)
        {
            if (string.IsNullOrWhiteSpace(filterJson) || filterJson!.Trim() == "{}")
                return Builders<BsonDocument>.Filter.Empty;

            var doc = BsonDocument.Parse(filterJson);
            return new BsonDocumentFilterDefinition<BsonDocument>(doc);
        }
    }
}
