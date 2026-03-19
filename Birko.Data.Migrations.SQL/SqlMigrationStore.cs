using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
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
            : this(connectionFactory, new SqlMigrationSettings
            {
                Location = remoteSettings.Location,
                Port = remoteSettings.Port,
                Name = remoteSettings.Name,
                UserName = remoteSettings.UserName,
                Password = remoteSettings.Password
            })
        {
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
        public async Task InitializeAsync()
        {
            using var connection = _connectionFactory();
            await connection.OpenAsync();

            var tableExists = await TableExistsAsync(connection);
            if (!tableExists)
            {
                await CreateMigrationsTableAsync(connection);
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
        public async Task<ISet<long>> GetAppliedVersionsAsync()
        {
            using var connection = _connectionFactory();
            await connection.OpenAsync();

            return await GetAppliedVersionsAsync(connection);
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
        public async Task RecordMigrationAsync(Data.Migrations.IMigration migration)
        {
            using var connection = _connectionFactory();
            await connection.OpenAsync();

            using var transaction = await connection.BeginTransactionAsync();
            try
            {
                await RecordMigrationAsync(connection, transaction, migration);
                await transaction.CommitAsync();
            }
            catch
            {
                await transaction.RollbackAsync();
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
        public async Task RemoveMigrationAsync(Data.Migrations.IMigration migration)
        {
            using var connection = _connectionFactory();
            await connection.OpenAsync();

            using var transaction = await connection.BeginTransactionAsync();
            try
            {
                await RemoveMigrationAsync(connection, transaction, migration);
                await transaction.CommitAsync();
            }
            catch
            {
                await transaction.RollbackAsync();
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
        public async Task<long> GetCurrentVersionAsync()
        {
            var versions = await GetAppliedVersionsAsync();
            return versions.Any() ? versions.Max() : 0;
        }

        #region Private Methods

        private bool TableExists(DbConnection connection)
        {
            var schema = connection.GetSchema("Tables");
            var tableName = _settings.MigrationsTable;

            foreach (DataRow row in schema.Rows)
            {
                var schemaName = row["TABLE_SCHEMA"] as string;
                var currentTableName = row["TABLE_NAME"] as string;

                if (string.Equals(currentTableName, tableName, StringComparison.OrdinalIgnoreCase))
                {
                    if (string.IsNullOrEmpty(_settings.Schema) || string.Equals(schemaName, _settings.Schema, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        private async Task<bool> TableExistsAsync(DbConnection connection)
        {
            var table = await connection.GetSchemaAsync("Tables");
            var tableName = _settings.MigrationsTable;

            foreach (DataRow row in table.Rows)
            {
                var schemaName = row["TABLE_SCHEMA"] as string;
                var currentTableName = row["TABLE_NAME"] as string;

                if (string.Equals(currentTableName, tableName, StringComparison.OrdinalIgnoreCase))
                {
                    if (string.IsNullOrEmpty(_settings.Schema) || string.Equals(schemaName, _settings.Schema, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }
            return false;
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

        private async Task CreateMigrationsTableAsync(DbConnection connection)
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
            await command.ExecuteNonQueryAsync();
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

        private async Task<ISet<long>> GetAppliedVersionsAsync(DbConnection connection)
        {
            var result = new HashSet<long>();

            if (!await TableExistsAsync(connection))
            {
                return result;
            }

            using var command = connection.CreateCommand();
            command.CommandText = $"SELECT {_quoteOpen}Version{_quoteClose} FROM {_settings.FullTableName}";

            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                result.Add(reader.GetInt64(0));
            }

            return result;
        }

        private void RecordMigration(DbConnection connection, DbTransaction transaction, Data.Migrations.IMigration migration)
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

        private async Task RecordMigrationAsync(DbConnection connection, DbTransaction transaction, Data.Migrations.IMigration migration)
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

            await command.ExecuteNonQueryAsync();
        }

        private void RemoveMigration(DbConnection connection, DbTransaction transaction, Data.Migrations.IMigration migration)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $"DELETE FROM {_settings.FullTableName} WHERE {_quoteOpen}Version{_quoteClose} = @Version;";
            AddParameter(command, "@Version", migration.Version);
            command.ExecuteNonQuery();
        }

        private async Task RemoveMigrationAsync(DbConnection connection, DbTransaction transaction, Data.Migrations.IMigration migration)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $"DELETE FROM {_settings.FullTableName} WHERE {_quoteOpen}Version{_quoteClose} = @Version;";
            AddParameter(command, "@Version", migration.Version);
            await command.ExecuteNonQueryAsync();
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
