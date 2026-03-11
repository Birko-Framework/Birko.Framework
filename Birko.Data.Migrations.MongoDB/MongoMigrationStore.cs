using MongoDB.Driver;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Birko.Data.Migrations.MongoDB
{
    /// <summary>
    /// Stores migration state in a MongoDB collection.
    /// </summary>
    public class MongoMigrationStore : Data.Migrations.IMigrationStore
    {
        private readonly IMongoClient _client;
        private readonly IMongoDatabase _database;
        private readonly Settings.MongoMigrationSettings _settings;

        private IMongoCollection<MigrationDocument>? _collection;

        /// <summary>
        /// Initializes a new instance of the MongoMigrationStore class.
        /// </summary>
        public MongoMigrationStore(IMongoClient client, IMongoDatabase database, Settings.MongoMigrationSettings? settings = null)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _database = database ?? throw new ArgumentNullException(nameof(database));
            _settings = settings ?? new Settings.MongoMigrationSettings();
        }

        /// <summary>
        /// Initializes the migration store (creates migrations collection if needed).
        /// </summary>
        public void Initialize()
        {
            var collectionName = _settings.MigrationsCollection;

            // Check if collection exists
            var filter = new BsonDocument("name", collectionName);
            var collections = _database.ListCollections(new ListCollectionsOptions { Filter = filter });

            if (!collections.Any())
            {
                _database.CreateCollection(collectionName);

                // Create index on Version field
                var collection = _database.GetCollection<MigrationDocument>(collectionName);
                var keys = Builders<MigrationDocument>.IndexKeys.Ascending(d => d.Version);
                collection.Indexes.CreateOne(keys, new CreateIndexOptions { Unique = true });
            }

            _collection = _database.GetCollection<MigrationDocument>(collectionName);
        }

        /// <summary>
        /// Asynchronously initializes the migration store.
        /// </summary>
        public Task InitializeAsync()
        {
            Initialize();
            return Task.CompletedTask;
        }

        /// <summary>
        /// Gets all applied migration versions.
        /// </summary>
        public ISet<long> GetAppliedVersions()
        {
            EnsureCollectionExists();

            var versions = _collection!
                .Find(FilterDefinition<MigrationDocument>.Empty)
                .Project(d => d.Version)
                .ToList();

            return new HashSet<long>(versions);
        }

        /// <summary>
        /// Asynchronously gets all applied migration versions.
        /// </summary>
        public Task<ISet<long>> GetAppliedVersionsAsync()
        {
            return Task.FromResult(GetAppliedVersions());
        }

        /// <summary>
        /// Records that a migration has been applied.
        /// </summary>
        public void RecordMigration(Data.Migrations.IMigration migration)
        {
            EnsureCollectionExists();

            var document = new MigrationDocument
            {
                Id = migration.Version.ToString(),
                Version = migration.Version,
                Name = migration.Name,
                Description = migration.Description,
                CreatedAt = migration.CreatedAt,
                AppliedAt = DateTime.UtcNow
            };

            var filter = Builders<MigrationDocument>.Filter.Eq(d => d.Id, document.Id);
            var options = new ReplaceOptions { IsUpsert = true };
            _collection!.ReplaceOne(filter, document, options);
        }

        /// <summary>
        /// Asynchronously records that a migration has been applied.
        /// </summary>
        public Task RecordMigrationAsync(Data.Migrations.IMigration migration)
        {
            RecordMigration(migration);
            return Task.CompletedTask;
        }

        /// <summary>
        /// Removes a migration record (when downgrading).
        /// </summary>
        public void RemoveMigration(Data.Migrations.IMigration migration)
        {
            EnsureCollectionExists();

            var filter = Builders<MigrationDocument>.Filter.Eq(d => d.Id, migration.Version.ToString());
            _collection!.DeleteOne(filter);
        }

        /// <summary>
        /// Asynchronously removes a migration record.
        /// </summary>
        public Task RemoveMigrationAsync(Data.Migrations.IMigration migration)
        {
            RemoveMigration(migration);
            return Task.CompletedTask;
        }

        /// <summary>
        /// Gets the current version of the database.
        /// </summary>
        public long GetCurrentVersion()
        {
            var versions = GetAppliedVersions();
            return versions.Any() ? versions.Max() : 0;
        }

        /// <summary>
        /// Asynchronously gets the current version.
        /// </summary>
        public Task<long> GetCurrentVersionAsync()
        {
            return Task.FromResult(GetCurrentVersion());
        }

        private void EnsureCollectionExists()
        {
            if (_collection == null)
            {
                Initialize();
            }
        }

        /// <summary>
        /// Internal document class for storing migration records.
        /// </summary>
        internal class MigrationDocument
        {
            [BsonId]
            public string Id { get; set; } = string.Empty;

            [BsonElement("version")]
            public long Version { get; set; }

            [BsonElement("name")]
            public string Name { get; set; } = string.Empty;

            [BsonElement("description")]
            public string Description { get; set; } = string.Empty;

            [BsonElement("createdAt")]
            public DateTime CreatedAt { get; set; }

            [BsonElement("appliedAt")]
            public DateTime AppliedAt { get; set; }
        }
    }
}
