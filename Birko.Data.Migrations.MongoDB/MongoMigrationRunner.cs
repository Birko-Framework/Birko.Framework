using MongoDB.Driver;
using MongoDB.Driver.Core.Clusters;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Birko.Data.Migrations.MongoDB
{
    /// <summary>
    /// Executes MongoDB migrations.
    /// </summary>
    public class MongoMigrationRunner : Data.Migrations.AbstractMigrationRunner
    {
        private readonly IMongoClient _client;
        private readonly IMongoDatabase _database;
        private readonly Settings.MongoMigrationSettings _settings;

        /// <summary>
        /// Gets the MongoDB client.
        /// </summary>
        public IMongoClient Client => _client;

        /// <summary>
        /// Gets the MongoDB database.
        /// </summary>
        public IMongoDatabase Database => _database;

        /// <summary>
        /// Initializes a new instance of the MongoMigrationRunner class.
        /// </summary>
        public MongoMigrationRunner(IMongoClient client, IMongoDatabase database, Settings.MongoMigrationSettings? settings = null)
            : base(new MongoMigrationStore(client, database, settings))
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _database = database ?? throw new ArgumentNullException(nameof(database));
            _settings = settings ?? new Settings.MongoMigrationSettings();
        }

        /// <summary>
        /// Executes migrations in the specified direction.
        /// </summary>
        protected override Data.Migrations.MigrationResult ExecuteMigrations(long fromVersion, long toVersion, Data.Migrations.MigrationDirection direction)
        {
            var migrations = GetMigrationsToExecute(fromVersion, toVersion, direction);
            var executed = new List<Data.Migrations.ExecutedMigration>();

            if (!migrations.Any())
            {
                return Data.Migrations.MigrationResult.Successful(fromVersion, toVersion, direction, executed);
            }

            var store = (MongoMigrationStore)Store;

            // Use session if enabled and available (requires replica set)
            IClientSessionHandle? session = null;
            try
            {
                if (_settings.UseSession && _client.Cluster.Description.Type == ClusterType.ReplicaSet)
                {
                    session = _client.StartSession();
                    session.StartTransaction();
                }

                foreach (var migration in migrations)
                {
                    if (migration is MongoMigration mongoMigration)
                    {
                        mongoMigration.Execute(_client, _database, direction);
                    }
                    else if (direction == Data.Migrations.MigrationDirection.Up)
                    {
                        migration.Up();
                    }
                    else
                    {
                        migration.Down();
                    }

                    // Update store record
                    if (direction == Data.Migrations.MigrationDirection.Up)
                    {
                        store.RecordMigration(migration);
                    }
                    else
                    {
                        store.RemoveMigration(migration);
                    }

                    executed.Add(new Data.Migrations.ExecutedMigration(migration, direction));
                }

                session?.CommitTransaction();

                return Data.Migrations.MigrationResult.Successful(fromVersion, toVersion, direction, executed);
            }
            catch (Exception ex)
            {
                try
                {
                    session?.AbortTransaction();
                }
                catch { }

                var failedMigration = executed.Count > 0 ? migrations[executed.Count] : migrations[0];
                throw new Exceptions.MigrationException(failedMigration, direction, "Migration failed. Changes rolled back if session was used.", ex);
            }
            finally
            {
                session?.Dispose();
            }
        }
    }
}
