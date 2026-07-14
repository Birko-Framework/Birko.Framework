using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Birko.Data.Migrations.SQL.Settings;

namespace Birko.Data.Migrations.SQL
{
    /// <summary>
    /// Stores migration state in a SQL database.
    /// </summary>
    public class SqlMigrationStore : Data.Migrations.IMigrationStore
    {
        private readonly Func<DbConnection> _connectionFactory;
        private readonly SqlMigrationSettings _settings;
        private readonly string _quoteOpen;
        private readonly string _quoteClose;

        /// <summary>
        /// Initializes a new instance of the SqlMigrationStore class.
        /// </summary>
        /// <param name="connectionFactory">Factory function to create database connections.</param>
        /// <param name="settings">Migration settings.</param>
        /// <param name="quoteOpen">Opening quote character for identifiers (e.g., "[" or "\"").</param>
        /// <param name="quoteClose">Closing quote character for identifiers.</param>
        public SqlMigrationStore(
            Func<DbConnection> connectionFactory,
            SqlMigrationSettings? settings = null,
            string quoteOpen = "\"",
            string quoteClose = "\"")
        {
            _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
            _settings = settings ?? new SqlMigrationSettings();
            _quoteOpen = quoteOpen;
            _quoteClose = quoteClose;
        }

        /// <summary>
        /// Initializes a new instance of the SqlMigrationStore class with PasswordSettings.
        /// </summary>
        public SqlMigrationStore(Func<DbConnection> connectionFactory, Birko.Configuration.RemoteSettings remoteSettings)
            : this(connectionFactory, CreateSettings(remoteSettings))
        {
        }

        // CR-L151: copy the whole inherited RemoteSettings chain via LoadFrom rather than hand-listing
        // properties (the manual copy silently dropped fields not enumerated, e.g. UseSecure, and had to
        // be updated whenever the settings chain grew).
        private static SqlMigrationSettings CreateSettings(Birko.Configuration.RemoteSettings remoteSettings)
        {
            var settings = new SqlMigrationSettings();
            settings.LoadFrom(remoteSettings);
            return settings;
        }

        /// <summary>
        /// Initializes the migration store (creates migrations table if needed).
        /// </summary>
        public void Initialize()
        {
            using var connection = _connectionFactory();
            connection.Open();

            var tableExists = TableExists(connection);
            if (!tableExists)
            {
                CreateMigrationsTable(connection);
            }
        }

        /// <summary>
        /// Asynchronously initializes the migration store.
        /// </summary>
        public async Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            using var connection = _connectionFactory();
            await connection.OpenAsync(cancellationToken);

            var tableExists = await TableExistsAsync(connection, cancellationToken);
            if (!tableExists)
            {
                await CreateMigrationsTableAsync(connection, cancellationToken);
            }
        }

        /// <summary>
        /// Gets all applied migration versions.
        /// </summary>
        public ISet<long> GetAppliedVersions()
        {
            using var connection = _connectionFactory();
            connection.Open();

            return GetAppliedVersions(connection);
        }

        /// <summary>
        /// Asynchronously gets all applied migration versions.
        /// </summary>
        public async Task<ISet<long>> GetAppliedVersionsAsync(CancellationToken cancellationToken = default)
        {
            using var connection = _connectionFactory();
            await connection.OpenAsync(cancellationToken);

            return await GetAppliedVersionsAsync(connection, cancellationToken);
        }

        /// <summary>
        /// Records that a migration has been applied.
        /// </summary>
        public void RecordMigration(Data.Migrations.IMigration migration)
        {
            using var connection = _connectionFactory();
            connection.Open();

            using var transaction = connection.BeginTransaction();
            try
            {
                RecordMigration(connection, transaction, migration);
                transaction.Commit();
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }

        /// <summary>
        /// Asynchronously records that a migration has been applied.
        /// </summary>
        public async Task RecordMigrationAsync(Data.Migrations.IMigration migration, CancellationToken cancellationToken = default)
        {
            using var connection = _connectionFactory();
            await connection.OpenAsync(cancellationToken);

            using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            try
            {
                await RecordMigrationAsync(connection, transaction, migration, cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken);
                throw;
            }
        }

        /// <summary>
        /// Removes a migration record (when downgrading).
        /// </summary>
        public void RemoveMigration(Data.Migrations.IMigration migration)
        {
            using var connection = _connectionFactory();
            connection.Open();

            using var transaction = connection.BeginTransaction();
            try
            {
                RemoveMigration(connection, transaction, migration);
                transaction.Commit();
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }

        /// <summary>
        /// Asynchronously removes a migration record.
        /// </summary>
        public async Task RemoveMigrationAsync(Data.Migrations.IMigration migration, CancellationToken cancellationToken = default)
        {
            using var connection = _connectionFactory();
            await connection.OpenAsync(cancellationToken);

            using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            try
            {
                await RemoveMigrationAsync(connection, transaction, migration, cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken);
                throw;
            }
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
        /// Asynchronously gets the current version of the database.
        /// </summary>
        public async Task<long> GetCurrentVersionAsync(CancellationToken cancellationToken = default)
        {
            var versions = await GetAppliedVersionsAsync(cancellationToken);
            return versions.Any() ? versions.Max() : 0;
        }

        #region Private Methods

        // Probe for the migrations table by selecting against it rather than reading the schema catalog:
        // DbConnection.GetSchema("Tables") is not implemented by every ADO.NET provider (notably
        // Microsoft.Data.Sqlite, which throws "The requested collection 'Tables' is not defined"), whereas
        // a guarded "SELECT ... WHERE 1=0" is portable across SQLite/MySQL/Postgres/MSSql — it touches no
        // rows and fails only when the table is absent, which is exactly the signal we want.
        // Only DbException is treated as "absent" (every ADO provider surfaces a missing table as a
        // DbException subclass); other exceptions — e.g. a misused/closed connection — propagate rather
        // than being silently read as "no table". A transient DbException (e.g. a lock) is still read as
        // absent; callers create this table with the runner single-threaded at startup, where that is moot.
        private bool TableExists(DbConnection connection)
        {
            try
            {
                using var command = connection.CreateCommand();
                command.CommandText = $"SELECT 1 FROM {_settings.FullTableName} WHERE 1 = 0";
                command.ExecuteNonQuery();
                return true;
            }
            catch (DbException)
            {
                return false;
            }
        }

        private async Task<bool> TableExistsAsync(DbConnection connection, CancellationToken cancellationToken)
        {
            try
            {
                using var command = connection.CreateCommand();
                command.CommandText = $"SELECT 1 FROM {_settings.FullTableName} WHERE 1 = 0";
                await command.ExecuteNonQueryAsync(cancellationToken);
                return true;
            }
            catch (DbException)
            {
                return false;
            }
        }

        private void CreateMigrationsTable(DbConnection connection)
        {
            var schema = _settings.Schema;
            var table = _settings.MigrationsTable;
            var fullTableName = _settings.FullTableName;

            using var command = connection.CreateCommand();
            command.CommandText = $@"
                CREATE TABLE {fullTableName} (
                    {_quoteOpen}Version{_quoteClose} BIGINT PRIMARY KEY,
                    {_quoteOpen}Name{_quoteClose} VARCHAR(255) NOT NULL,
                    {_quoteOpen}Description{_quoteClose} TEXT,
                    {_quoteOpen}CreatedAt{_quoteClose} TIMESTAMP NOT NULL,
                    {_quoteOpen}AppliedAt{_quoteClose} TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP
                );";
            command.ExecuteNonQuery();
        }

        private async Task CreateMigrationsTableAsync(DbConnection connection, CancellationToken cancellationToken)
        {
            var fullTableName = _settings.FullTableName;

            using var command = connection.CreateCommand();
            command.CommandText = $@"
                CREATE TABLE {fullTableName} (
                    {_quoteOpen}Version{_quoteClose} BIGINT PRIMARY KEY,
                    {_quoteOpen}Name{_quoteClose} VARCHAR(255) NOT NULL,
                    {_quoteOpen}Description{_quoteClose} TEXT,
                    {_quoteOpen}CreatedAt{_quoteClose} TIMESTAMP NOT NULL,
                    {_quoteOpen}AppliedAt{_quoteClose} TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP
                );";
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        private ISet<long> GetAppliedVersions(DbConnection connection)
        {
            var result = new HashSet<long>();

            if (!TableExists(connection))
            {
                return result;
            }

            using var command = connection.CreateCommand();
            command.CommandText = $"SELECT {_quoteOpen}Version{_quoteClose} FROM {_settings.FullTableName}";

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                result.Add(reader.GetInt64(0));
            }

            return result;
        }

        private async Task<ISet<long>> GetAppliedVersionsAsync(DbConnection connection, CancellationToken cancellationToken)
        {
            var result = new HashSet<long>();

            if (!await TableExistsAsync(connection, cancellationToken))
            {
                return result;
            }

            using var command = connection.CreateCommand();
            command.CommandText = $"SELECT {_quoteOpen}Version{_quoteClose} FROM {_settings.FullTableName}";

            using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                result.Add(reader.GetInt64(0));
            }

            return result;
        }

        // Records a version row on a caller-supplied connection (and optional transaction) instead of
        // opening its own. The runner uses this so the version write participates in the same
        // connection/transaction as the migration DDL — see SqlMigrationRunner.UpdateStoreRecord for
        // why that matters on single-writer SQLite.
        internal void RecordMigration(DbConnection connection, DbTransaction? transaction, Data.Migrations.IMigration migration)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $@"
                INSERT INTO {_settings.FullTableName}
                ({_quoteOpen}Version{_quoteClose}, {_quoteOpen}Name{_quoteClose}, {_quoteOpen}Description{_quoteClose}, {_quoteOpen}CreatedAt{_quoteClose})
                VALUES (@Version, @Name, @Description, @CreatedAt);";

            AddParameter(command, "@Version", migration.Version);
            AddParameter(command, "@Name", migration.Name);
            AddParameter(command, "@Description", migration.Description);
            AddParameter(command, "@CreatedAt", migration.CreatedAt);

            command.ExecuteNonQuery();
        }

        private async Task RecordMigrationAsync(DbConnection connection, DbTransaction transaction, Data.Migrations.IMigration migration, CancellationToken cancellationToken)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $@"
                INSERT INTO {_settings.FullTableName}
                ({_quoteOpen}Version{_quoteClose}, {_quoteOpen}Name{_quoteClose}, {_quoteOpen}Description{_quoteClose}, {_quoteOpen}CreatedAt{_quoteClose})
                VALUES (@Version, @Name, @Description, @CreatedAt);";

            AddParameter(command, "@Version", migration.Version);
            AddParameter(command, "@Name", migration.Name);
            AddParameter(command, "@Description", migration.Description);
            AddParameter(command, "@CreatedAt", migration.CreatedAt);

            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        // Removes a version row on a caller-supplied connection (and optional transaction); the
        // connection-reusing counterpart to RecordMigration above, used by the runner on downgrade.
        internal void RemoveMigration(DbConnection connection, DbTransaction? transaction, Data.Migrations.IMigration migration)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $"DELETE FROM {_settings.FullTableName} WHERE {_quoteOpen}Version{_quoteClose} = @Version;";
            AddParameter(command, "@Version", migration.Version);
            command.ExecuteNonQuery();
        }

        private async Task RemoveMigrationAsync(DbConnection connection, DbTransaction transaction, Data.Migrations.IMigration migration, CancellationToken cancellationToken)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $"DELETE FROM {_settings.FullTableName} WHERE {_quoteOpen}Version{_quoteClose} = @Version;";
            AddParameter(command, "@Version", migration.Version);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        private void AddParameter(DbCommand command, string name, object? value)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = name;
            parameter.Value = value ?? DBNull.Value;
            command.Parameters.Add(parameter);
        }

        #endregion
    }
}
