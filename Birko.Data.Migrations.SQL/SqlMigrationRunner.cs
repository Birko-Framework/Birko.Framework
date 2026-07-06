using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using Birko.Data.Migrations.Exceptions;
using Birko.Data.Migrations.SQL.Settings;
using Birko.Data.SQL.Connectors;

namespace Birko.Data.Migrations.SQL
{
    /// <summary>
    /// Executes migrations against a SQL database.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Failure model: a migration that throws surfaces as a <see cref="MigrationException"/> (under
    /// <c>UseTransaction = true</c> the batch is rolled back first). The runner does <b>not</b>
    /// return a failed <see cref="Data.Migrations.MigrationResult"/> for an execution error — a
    /// non-successful result is only produced by the target/downgrade guard — so callers wrap
    /// <c>Migrate()</c> in <c>try/catch (MigrationException)</c> rather than checking
    /// <c>result.Success</c>.
    /// </para>
    /// <para>
    /// Version bookkeeping shares the runner's connection/transaction (see
    /// <c>UpdateStoreRecord</c>), so the default <c>UseTransaction = true</c> is safe on
    /// single-writer SQLite — there is no second-connection lock contention to work around.
    /// </para>
    /// </remarks>
    public class SqlMigrationRunner : Data.Migrations.AbstractMigrationRunner
    {
        private readonly AbstractConnector _connector;
        private readonly SqlMigrationSettings _settings;

        /// <summary>
        /// Initializes a new instance of the SqlMigrationRunner class.
        /// </summary>
        /// <param name="connector">SQL connector from the store. Use <c>store.Connector</c> to pass it.</param>
        /// <param name="settings">Migration settings.</param>
        public SqlMigrationRunner(AbstractConnector connector, SqlMigrationSettings? settings = null)
            : base(new SqlMigrationStore(() => connector.CreateConnection(connector.Settings), settings))
        {
            _connector = connector ?? throw new ArgumentNullException(nameof(connector));
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

            using var connection = _connector.CreateConnection(_connector.Settings);
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

        protected virtual void ExecuteSingleMigration(
            Data.Migrations.IMigration migration,
            Data.Migrations.MigrationDirection direction,
            DbConnection connection,
            DbTransaction? transaction)
        {
            var context = new Context.SqlMigrationContext(connection, transaction, "SQL", _connector);
            if (direction == Data.Migrations.MigrationDirection.Up)
                migration.Up(context);
            else
                migration.Down(context);
        }

        private void UpdateStoreRecord(
            Data.Migrations.IMigration migration,
            Data.Migrations.MigrationDirection direction,
            SqlMigrationStore store,
            DbConnection connection,
            DbTransaction? transaction)
        {
            // Record the applied/removed version on the runner's OWN already-open connection (and
            // active transaction, if any) rather than letting the store open a second connection.
            // On single-writer databases (SQLite) the outer migration transaction still holds the
            // write lock from the DDL, so a second connection's INSERT would block and fail
            // (SQLITE_BUSY) — the reason consumers previously had to set UseTransaction = false.
            // Reusing the connection also makes the DDL + version row atomic under a transaction.
            if (direction == Data.Migrations.MigrationDirection.Up)
            {
                store.RecordMigration(connection, transaction, migration);
            }
            else
            {
                store.RemoveMigration(connection, transaction, migration);
            }
        }
    }
}
