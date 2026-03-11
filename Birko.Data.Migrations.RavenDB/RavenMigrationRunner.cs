using Raven.Client.Documents;
using Raven.Client.Documents.Session;
using System;
using System.Collections.Generic;

namespace Birko.Data.Migrations.RavenDB
{
    /// <summary>
    /// Executes RavenDB migrations.
    /// </summary>
    public class RavenMigrationRunner : Data.Migrations.AbstractMigrationRunner
    {
        private readonly IDocumentStore _store;

        /// <summary>
        /// Gets the RavenDB document store.
        /// </summary>
        public IDocumentStore DocumentStore => _store;

        /// <summary>
        /// Initializes a new instance of the RavenMigrationRunner class.
        /// </summary>
        public RavenMigrationRunner(IDocumentStore store)
            : base(new RavenMigrationStore(store))
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
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

            var store = (RavenMigrationStore)Store;

            // RavenDB uses implicit transactions in sessions
            try
            {
                foreach (var migration in migrations)
                {
                    if (migration is RavenMigration ravenMigration)
                    {
                        ravenMigration.Execute(_store, direction);
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
                throw new Exceptions.MigrationException(failedMigration, direction, "Migration failed. RavenDB state may be inconsistent.", ex);
            }
        }
    }
}
