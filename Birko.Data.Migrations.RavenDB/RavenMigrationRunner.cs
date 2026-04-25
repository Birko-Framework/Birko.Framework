using Raven.Client.Documents;
using System;
using System.Collections.Generic;
using Birko.Data.Migrations.RavenDB.Settings;

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
        /// <param name="store">RavenDB document store.</param>
        /// <param name="settings">Optional settings controlling the state document id
        /// (for per-module isolation within one database).</param>
        public RavenMigrationRunner(IDocumentStore store, RavenMigrationSettings? settings = null)
            : base(new RavenMigrationStore(store, settings))
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

            try
            {
                foreach (var migration in migrations)
                {
                    var context = new Context.RavenDBMigrationContext(_store);
                    if (direction == Data.Migrations.MigrationDirection.Up)
                        migration.Up(context);
                    else
                        migration.Down(context);

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
