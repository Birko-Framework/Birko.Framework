using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Threading.Tasks;
using Birko.Data.Migrations.Exceptions;
using Birko.Data.Migrations.SQL.Settings;

namespace Birko.Data.Migrations.SQL
{
    /// <summary>
    /// Executes migrations against a SQL database.
    /// </summary>
    public class SqlMigrationRunner : Data.Migrations.AbstractMigrationRunner
    {
        private readonly Func<DbConnection> _connectionFactory;
        private readonly SqlMigrationSettings _settings;

        /// <summary>
        /// Initializes a new instance of the SqlMigrationRunner class.
        /// </summary>
        /// <param name="connectionFactory">Factory function to create database connections.</param>
        /// <param name="settings">Migration settings.</param>
        public SqlMigrationRunner(Func<DbConnection> connectionFactory, SqlMigrationSettings? settings = null)
            : base(new SqlMigrationStore(connectionFactory, settings))
        {
            _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
            _settings = settings ?? new SqlMigrationSettings();
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

            using var connection = _connectionFactory();
            connection.Open();

            if (_settings.UseTransaction)
            {
                return ExecuteWithTransaction(connection, migrations, direction, fromVersion, toVersion);
            }
            else
            {
                return ExecuteWithoutTransaction(connection, migrations, direction, fromVersion, toVersion);
            }
        }

        private Data.Migrations.MigrationResult ExecuteWithTransaction(
            DbConnection connection,
            IReadOnlyList<Data.Migrations.IMigration> migrations,
            Data.Migrations.MigrationDirection direction,
            long fromVersion,
            long toVersion)
        {
            using var transaction = connection.BeginTransaction();
            var executed = new List<Data.Migrations.ExecutedMigration>();

            try
            {
                var store = (SqlMigrationStore)Store;

                foreach (var migration in migrations)
                {
                    ExecuteSingleMigration(migration, direction, connection, transaction);
                    UpdateStoreRecord(migration, direction, store, connection, transaction);
                    executed.Add(new Data.Migrations.ExecutedMigration(migration, direction));
                }

                transaction.Commit();
                return Data.Migrations.MigrationResult.Successful(fromVersion, toVersion, direction, executed);
            }
            catch (Exception ex)
            {
                try
                {
                    transaction.Rollback();
                }
                catch
                {
                    // Ignore rollback errors
                }

                var failedMigration = executed.Count > 0 ? migrations[executed.Count] : migrations[0];
                throw new MigrationException(failedMigration, direction, "Migration failed. Changes have been rolled back.", ex);
            }
        }

        private Data.Migrations.MigrationResult ExecuteWithoutTransaction(
            DbConnection connection,
            IReadOnlyList<Data.Migrations.IMigration> migrations,
            Data.Migrations.MigrationDirection direction,
            long fromVersion,
            long toVersion)
        {
            var executed = new List<Data.Migrations.ExecutedMigration>();
            var store = (SqlMigrationStore)Store;

            try
            {
                foreach (var migration in migrations)
                {
                    ExecuteSingleMigration(migration, direction, connection, null);
                    UpdateStoreRecord(migration, direction, store, connection, null);
                    executed.Add(new Data.Migrations.ExecutedMigration(migration, direction));
                }

                return Data.Migrations.MigrationResult.Successful(fromVersion, toVersion, direction, executed);
            }
            catch (Exception ex)
            {
                var failedMigration = executed.Count > 0 ? migrations[executed.Count] : migrations[0];
                throw new MigrationException(failedMigration, direction, "Migration failed. Database may be in an inconsistent state.", ex);
            }
        }

        private void ExecuteSingleMigration(
            Data.Migrations.IMigration migration,
            Data.Migrations.MigrationDirection direction,
            DbConnection connection,
            DbTransaction? transaction)
        {
            if (migration is SqlMigration sqlMigration)
            {
                // SQL migration can use the connection directly
                sqlMigration.Execute(connection, transaction, direction);
            }
            else if (direction == Data.Migrations.MigrationDirection.Up)
            {
                migration.Up();
            }
            else
            {
                migration.Down();
            }
        }

        private void UpdateStoreRecord(
            Data.Migrations.IMigration migration,
            Data.Migrations.MigrationDirection direction,
            SqlMigrationStore store,
            DbConnection connection,
            DbTransaction? transaction)
        {
            if (direction == Data.Migrations.MigrationDirection.Up)
            {
                store.RecordMigration(migration);
            }
            else
            {
                store.RemoveMigration(migration);
            }
        }
    }
}
