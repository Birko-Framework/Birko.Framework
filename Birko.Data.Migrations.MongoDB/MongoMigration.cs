using MongoDB.Driver;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Birko.Data.Migrations.MongoDB
{
    /// <summary>
    /// Abstract base class for MongoDB migrations.
    /// </summary>
    public abstract class MongoMigration : Data.Migrations.AbstractMigration
    {
        /// <summary>
        /// Applies the migration using the MongoDB client.
        /// </summary>
        /// <param name="client">The MongoDB client.</param>
        /// <param name="database">The MongoDB database.</param>
        protected abstract void Up(IMongoClient client, IMongoDatabase database);

        /// <summary>
        /// Reverts the migration using the MongoDB client.
        /// </summary>
        /// <param name="client">The MongoDB client.</param>
        /// <param name="database">The MongoDB database.</param>
        protected abstract void Down(IMongoClient client, IMongoDatabase database);

        /// <summary>
        /// Throws exception - migrations require MongoDB context.
        /// </summary>
        public override void Up()
        {
            throw new InvalidOperationException("MongoMigration requires IMongoClient and IMongoDatabase. Use MongoMigrationRunner to execute migrations.");
        }

        /// <summary>
        /// Throws exception - migrations require MongoDB context.
        /// </summary>
        public override void Down()
        {
            throw new InvalidOperationException("MongoMigration requires IMongoClient and IMongoDatabase. Use MongoMigrationRunner to execute migrations.");
        }

        /// <summary>
        /// Internal execution method called by MongoMigrationRunner.
        /// </summary>
        internal void Execute(IMongoClient client, IMongoDatabase database, Data.Migrations.MigrationDirection direction)
        {
            if (direction == Data.Migrations.MigrationDirection.Up)
            {
                Up(client, database);
            }
            else
            {
                Down(client, database);
            }
        }

        /// <summary>
        /// Creates a collection.
        /// </summary>
        protected virtual void CreateCollection(IMongoDatabase database, string collectionName)
        {
            var filter = new BsonDocument("name", collectionName);
            var collections = database.ListCollections(new ListCollectionsOptions { Filter = filter });
            if (!collections.Any())
            {
                database.CreateCollection(collectionName);
            }
        }

        /// <summary>
        /// Drops a collection.
        /// </summary>
        protected virtual void DropCollection(IMongoDatabase database, string collectionName)
        {
            var filter = new BsonDocument("name", collectionName);
            var collections = database.ListCollections(new ListCollectionsOptions { Filter = filter });
            if (collections.Any())
            {
                database.DropCollection(collectionName);
            }
        }

        /// <summary>
        /// Creates an index on a collection.
        /// </summary>
        protected virtual string CreateIndex<T>(IMongoDatabase database, string collectionName, CreateIndexModel<T> indexModel)
        {
            var collection = database.GetCollection<T>(collectionName);
            return collection.Indexes.CreateOne(indexModel);
        }

        /// <summary>
        /// Creates an index using a keys definition.
        /// </summary>
        protected virtual string CreateIndex(IMongoDatabase database, string collectionName, string keysJson, CreateIndexOptions? options = null)
        {
            var collection = database.GetCollection<BsonDocument>(collectionName);
            var keys = BsonDocument.Parse(keysJson);
            return collection.Indexes.CreateOne(new BsonDocumentIndexKeysDefinition<BsonDocument>(keys), options);
        }

        /// <summary>
        /// Drops an index from a collection.
        /// </summary>
        protected virtual void DropIndex(IMongoDatabase database, string collectionName, string indexName)
        {
            var collection = database.GetCollection<BsonDocument>(collectionName);
            collection.Indexes.DropOne(indexName);
        }

        /// <summary>
        /// Drops all indexes from a collection except the default _id index.
        /// </summary>
        protected virtual void DropAllIndexes(IMongoDatabase database, string collectionName)
        {
            var collection = database.GetCollection<BsonDocument>(collectionName);
            collection.Indexes.DropAll();
        }

        /// <summary>
        /// Renames a collection.
        /// </summary>
        protected virtual void RenameCollection(IMongoDatabase database, string oldName, string newName)
        {
            database.RenameCollection(oldName, newName);
        }

        /// <summary>
        /// Updates documents matching a filter.
        /// </summary>
        protected virtual UpdateResult UpdateDocuments(IMongoDatabase database, string collectionName, FilterDefinition<BsonDocument> filter, UpdateDefinition<BsonDocument> update)
        {
            var collection = database.GetCollection<BsonDocument>(collectionName);
            return collection.UpdateMany(filter, update);
        }

        /// <summary>
        /// Executes a command on the database.
        /// </summary>
        protected virtual BsonDocument RunCommand(IMongoDatabase database, string commandJson)
        {
            var command = BsonDocument.Parse(commandJson);
            return database.RunCommand<BsonDocument>(command);
        }

        /// <summary>
        /// Creates or updates a validation rule for a collection.
        /// </summary>
        protected virtual void SetValidationRule(IMongoDatabase database, string collectionName, string validationRuleJson, DocumentValidationLevel validationLevel = DocumentValidationLevel.Moderate)
        {
            var command = new BsonDocument
            {
                { "collMod", collectionName },
                { "validator", BsonDocument.Parse(validationRuleJson) },
                { "validationLevel", validationLevel.ToString() }
            };
            database.RunCommand<BsonDocument>(command);
        }

        /// <summary>
        /// Checks if a collection exists.
        /// </summary>
        protected virtual bool CollectionExists(IMongoDatabase database, string collectionName)
        {
            var filter = new BsonDocument("name", collectionName);
            var collections = database.ListCollections(new ListCollectionsOptions { Filter = filter });
            return collections.Any();
        }
    }
}
