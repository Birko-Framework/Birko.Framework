using InfluxDB.Client;
using System;
using System.Collections.Generic;

namespace Birko.Data.Migrations.InfluxDB
{
    /// <summary>
    /// Executes InfluxDB migrations.
    /// </summary>
    public class InfluxMigrationRunner : Data.Migrations.AbstractMigrationRunner
    {
        private readonly InfluxDBClient _client;
        private readonly string _organization;

        /// <summary>
        /// Gets the InfluxDB client.
        /// </summary>
        public InfluxDBClient Client => _client;

        /// <summary>
        /// Gets the organization name.
        /// </summary>
        public string Organization => _organization;

        /// <summary>
        /// Initializes a new instance of the InfluxMigrationRunner class.
        /// </summary>
        public InfluxMigrationRunner(InfluxDBClient client, string organization)
            : base(new InfluxMigrationStore(client, organization))
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _organization = organization ?? throw new ArgumentNullException(nameof(organization));
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

            var store = (InfluxMigrationStore)Store;

            try
            {
                foreach (var migration in migrations)
                {
                    if (migration is InfluxMigration influxMigration)
                    {
                        influxMigration.Execute(_client, _organization, direction);
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
                throw new Exceptions.MigrationException(failedMigration, direction, "Migration failed. InfluxDB state may be inconsistent.", ex);
            }
        }
    }
}
