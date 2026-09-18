using MongoDB.Driver;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Birko.Data.Migrations.MongoDB
{
    /// <summary>
    /// Stores migration state in a MongoDB collection.
    /// </summary>
    public class MongoMigrationStore : Data.Migrations.IMigrationStore
    {
        private readonly IMongoDatabase _database;
        private readonly Settings.MongoMigrationSettings _settings;

        private IMongoCollection<MigrationDocument>? _collection;
        private IClientSessionHandle? _session;

        /// <summary>
        /// Joins this store's bookkeeping writes to <paramref name="session"/> until the returned scope is
        /// disposed (SH-H031).
        ///
        /// <para>
        /// <b>Why.</b> <see cref="MongoMigrationRunner"/> threads the session into the migration context so
        /// each migration's own operations join the transaction, then recorded the version row through this
        /// store -- which issued a <b>sessionless</b> <c>ReplaceOne</c>. A sessionless driver call commits
        /// immediately, so when a later migration failed and <c>AbortTransaction()</c> rolled the data back,
        /// the version rows survived: those migrations were permanently considered applied while their
        /// changes were gone, and no re-run could repair it. The version row has to live or die with the
        /// data it describes.
        /// </para>
        /// <para>
        /// <b>Scoped, not assigned.</b> It restores the previous value on dispose rather than clearing to
        /// null, so a nested or repeated run cannot strand the store pointing at a session that has already
        /// been committed -- the trap § Conventions records for per-caller state left on a longer-lived
        /// object. <see cref="IMigrationStore"/> cannot carry the session in its signature without changing
        /// every backend, so it is ambient on the concrete store and nowhere else.
        /// </para>
        /// </summary>
        public IDisposable EnterSession(IClientSessionHandle? session)
        {
            var previous = _session;
            _session = session;
            return new SessionScope(this, previous);
        }

        private sealed class SessionScope : IDisposable
        {
            private readonly MongoMigrationStore _store;
            private readonly IClientSessionHandle? _previous;
            private bool _disposed;

            internal SessionScope(MongoMigrationStore store, IClientSessionHandle? previous)
            {
                _store = store;
                _previous = previous;
            }

            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                _store._session = _previous;
            }
        }

        /// <summary>
        /// Initializes a new instance of the MongoMigrationStore class.
        /// </summary>
        /// <remarks>
        /// CR-L148: the store operates entirely through <paramref name="database"/>. The previously-held
        /// <c>IMongoClient</c> field was never read (dead state), so the constructor no longer takes it.
        /// If session-aware transaction support is added later, thread the client back in then.
        /// </remarks>
        public MongoMigrationStore(IMongoDatabase database, Settings.MongoMigrationSettings? settings = null)
        {
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
                collection.Indexes.CreateOne(new CreateIndexModel<MigrationDocument>(keys, new CreateIndexOptions { Unique = true }));
            }

            _collection = _database.GetCollection<MigrationDocument>(collectionName);
        }

        /// <summary>
        /// Asynchronously initializes the migration store.
        /// </summary>
        public Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
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
        public Task<ISet<long>> GetAppliedVersionsAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
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
            // SH-H031: inside a runner transaction this must be the session overload, or the version row
            // commits immediately and survives the AbortTransaction that discards the data.
            if (_session != null)
                _collection!.ReplaceOne(_session, filter, document, options);
            else
                _collection!.ReplaceOne(filter, document, options);
        }

        /// <summary>
        /// Asynchronously records that a migration has been applied.
        /// </summary>
        public Task RecordMigrationAsync(Data.Migrations.IMigration migration, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
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
            // SH-H031 on the Down path -- same defect, not named in the finding: an aborted downgrade used
            // to leave the version row deleted while the data it described was restored.
            if (_session != null)
                _collection!.DeleteOne(_session, filter);
            else
                _collection!.DeleteOne(filter);
        }

        /// <summary>
        /// Asynchronously removes a migration record.
        /// </summary>
        public Task RemoveMigrationAsync(Data.Migrations.IMigration migration, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
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
        public Task<long> GetCurrentVersionAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
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
