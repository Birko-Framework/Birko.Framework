using System;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Birko.Data.MongoDB
{
    /// <summary>
    /// Wrapper around MongoDB C# Driver's MongoClient.
    /// </summary>
    public class MongoDBClient : IDisposable
    {
        /// <summary>
        /// Gets the MongoDB client.
        /// </summary>
        public MongoClient Client { get; private set; }

        /// <summary>
        /// Gets the MongoDB database.
        /// </summary>
        public IMongoDatabase Database { get; private set; }

        /// <summary>
        /// Gets the settings used for this connection.
        /// </summary>
        public Stores.Settings Settings { get; private set; }

        /// <summary>
        /// Initializes a new instance of the MongoDBClient class.
        /// </summary>
        /// <param name="settings">The MongoDB settings.</param>
        public MongoDBClient(Stores.Settings settings)
        {
            Settings = settings ?? throw new ArgumentNullException(nameof(settings));

            var connectionString = settings.GetConnectionString();
            Client = new MongoClient(connectionString);

            if (!string.IsNullOrEmpty(settings.Name))
            {
                Database = Client.GetDatabase(settings.Name);
            }
            else
            {
                throw new ArgumentException("Database name must be provided in settings", nameof(settings));
            }
        }

        /// <summary>
        /// Gets a MongoDB collection for the specified type.
        /// </summary>
        /// <typeparam name="T">The type of document in the collection.</typeparam>
        /// <param name="collectionName">Optional collection name. If not provided, uses the type name.</param>
        /// <returns>A MongoDB collection.</returns>
        public IMongoCollection<T> GetCollection<T>(string? collectionName = null)
        {
            var name = collectionName ?? typeof(T).Name;
            return Database.GetCollection<T>(name);
        }

        /// <summary>
        /// Checks if the MongoDB server is reachable.
        /// </summary>
        /// <returns>True if the server is reachable, false otherwise.</returns>
        public bool IsHealthy()
        {
            try
            {
                Client.GetDatabase("admin").RunCommand<BsonDocument>(new BsonDocument("ping", 1));
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Creates an index on a collection.
        /// </summary>
        /// <typeparam name="T">The document type.</typeparam>
        /// <param name="indexKeysDefinition">The index keys definition.</param>
        /// <param name="collectionName">Optional collection name.</param>
        public void CreateIndex<T>(IndexKeysDefinition<T> indexKeysDefinition, string? collectionName = null)
        {
            var collection = GetCollection<T>(collectionName);
            var indexOptions = new CreateIndexOptions { Background = true };
            collection.Indexes.CreateOne(new CreateIndexModel<T>(indexKeysDefinition, indexOptions));
        }

        /// <summary>
        /// Drops a collection.
        /// </summary>
        /// <typeparam name="T">The document type.</typeparam>
        /// <param name="collectionName">Optional collection name.</param>
        public void DropCollection<T>(string? collectionName = null)
        {
            var name = collectionName ?? typeof(T).Name;
            Database.DropCollection(name);
        }

        /// <summary>
        /// Disposes the MongoDB client.
        /// </summary>
        public void Dispose()
        {
            Client?.Dispose();
        }
    }
}
