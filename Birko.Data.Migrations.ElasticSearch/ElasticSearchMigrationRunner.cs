using Nest;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Birko.Data.Migrations.ElasticSearch
{
    /// <summary>
    /// Executes ElasticSearch migrations.
    /// </summary>
    public class ElasticSearchMigrationRunner : Data.Migrations.AbstractMigrationRunner
    {
        private readonly ElasticClient _client;

        /// <summary>
        /// Gets the ElasticSearch client.
        /// </summary>
        public ElasticClient Client => _client;

        /// <summary>
        /// Initializes a new instance of the ElasticSearchMigrationRunner class.
        /// </summary>
        public ElasticSearchMigrationRunner(ElasticClient client, Settings.ElasticSearchMigrationSettings? settings = null)
            : base(new ElasticSearchMigrationStore(client, settings))
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
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

            var store = (ElasticSearchMigrationStore)Store;

            try
            {
                foreach (var migration in migrations)
                {
                    if (migration is ElasticSearchMigration esMigration)
                    {
                        esMigration.Execute(_client, direction);
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

                return Data.Migrations.MigrationResult.Successful(fromVersion, toVersion, direction, executed);
            }
            catch (Exception ex)
            {
                var failedMigration = executed.Count > 0 ? migrations[executed.Count] : migrations[0];
                throw new Exceptions.MigrationException(failedMigration, direction, "Migration failed. ElasticSearch state may be inconsistent.", ex);
            }
        }
    }
}
