using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Birko.Data.Migrations.SQL.Context;
using Birko.Data.Migrations.SQL.Settings;
using Birko.Data.Patterns.Schema;
using Birko.Data.SQL.Connectors;

namespace Birko.Data.Migrations.SQL
{
    /// <summary>
    /// Stores migration state in a SQL database.
    /// </summary>
    public class SqlMigrationStore : Data.Migrations.IMigrationStore
    {
        private readonly Func<DbConnection> _connectionFactory;
        private readonly SqlMigrationSettings _settings;
        private readonly AbstractConnector _connector;

        /// <summary>
        /// Initializes a new instance of the SqlMigrationStore class.
        /// </summary>
        /// <param name="connectionFactory">Factory function to create database connections.</param>
        /// <param name="connector">
        /// The SQL connector for the target database. <b>Required</b> - it is the single producer of this
        /// store's identifier quoting and of its column types. Use <c>store.Connector</c>, or let
        /// <see cref="SqlMigrationRunner"/> supply the one it already holds.
        /// </param>
        /// <param name="settings">Migration settings.</param>
        /// <remarks>
        /// <b>TASK-332 - required, not optional, and the two quoting parameters it replaces are gone.</b>
        /// Those were <c>quoteOpen</c>/<c>quoteClose</c>, defaulting to an ANSI double quote, and <b>nothing
        /// anywhere passed them</b>: measured, 0 call sites across the framework, its tests and all 16
        /// consumer repos, so the default always won even though <see cref="SqlMigrationRunner"/> constructs
        /// this store while holding the connector. A fallback nobody can reach is not a safety net but a
        /// second implementation that drifts (&#167; TASK-247), and this one had drifted into being wrong on
        /// half the supported providers. Taking the connector instead means a dialect cannot be guessed:
        /// there is nothing left to guess with.
        /// </remarks>
        public SqlMigrationStore(
            Func<DbConnection> connectionFactory,
            AbstractConnector connector,
            SqlMigrationSettings? settings = null)
        {
            _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
            _connector = connector ?? throw new ArgumentNullException(
                nameof(connector),
                "SqlMigrationStore needs the connector to quote identifiers and choose column types for the "
              + "target dialect. The hardcoded ANSI defaults it used to fall back on were rejected outright by "
              + "MySQL (ERROR 1064) and declared a ROWVERSION column on SQL Server (Msg 2738). "
              + "SqlMigrationRunner already holds one - pass `store.Connector`.");
            _settings = settings ?? new SqlMigrationSettings();
        }

        /// <summary>
        /// Initializes a new instance of the SqlMigrationStore class with PasswordSettings.
        /// </summary>
        public SqlMigrationStore(Func<DbConnection> connectionFactory, AbstractConnector connector, Birko.Configuration.RemoteSettings remoteSettings)
            : this(connectionFactory, connector, CreateSettings(remoteSettings))
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
                command.CommandText = $"SELECT 1 FROM {TableReference} WHERE 1 = 0";
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
                command.CommandText = $"SELECT 1 FROM {TableReference} WHERE 1 = 0";
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
            using var command = connection.CreateCommand();
            command.CommandText = CreateMigrationsTableSql();
            command.ExecuteNonQuery();
        }

        private async Task CreateMigrationsTableAsync(DbConnection connection, CancellationToken cancellationToken)
        {
            using var command = connection.CreateCommand();
            command.CommandText = CreateMigrationsTableSql();
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        /// <summary>
        /// The migrations table's <c>CREATE TABLE</c>, rendered for the connector's dialect.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>TASK-332 - one producer, and it is deliberately shared by the sync and the async path.</b> This
        /// statement used to be written out twice, once in each, and the duplication was load-bearing: a fix
        /// applied to one would have left the other emitting the broken DDL, which is the shape
        /// &#167; Conventions keeps recording. Both callers now render from here.
        /// </para>
        /// <para>
        /// <b>Types come from <c>ConvertType</c>, the same method <c>AbstractConnector.CreateTable</c> uses
        /// for entity tables</b> - the one-producer rule TASK-269 states for the declared side of a column.
        /// The hardcoded types this replaced were not portable: <c>TIMESTAMP</c> in T-SQL is a deprecated
        /// synonym for <c>ROWVERSION</c>, a binary row-version type of which a table may have <b>at most
        /// one</b>, so two such columns were rejected outright - measured on SQL Server 2022 CU26
        /// (16.0.4275.2) as <i>"Msg 2738 ... A table can only have one timestamp column. Because table
        /// '__Migrations_X' already has one, the column 'AppliedAt' cannot be added."</i> <c>TEXT</c> in the
        /// same statement is deprecated there too and becomes <c>NVARCHAR(MAX)</c> by the same route
        /// (TASK-257). Going through <c>ConvertType</c> also means every future per-provider column-typing
        /// fix reaches this table without being restated here.
        /// </para>
        /// <para>
        /// <b>Column identifiers are quoted here, and that is NOT the entity-DDL rule.</b>
        /// <c>AbstractConnector.CreateTable</c> emits entity column definitions <i>bare</i> on purpose
        /// (&#167; TASK-245: a quoted column cannot resolve the case-folded one PostgreSQL stores), and this
        /// statement does not reach that path. Quoting is kept here because every existing PostgreSQL and
        /// SQLite deployment already has this table with quoted PascalCase columns; emitting them bare would
        /// fold <c>"Version"</c> to <c>version</c> on PostgreSQL, and every later read of an existing database
        /// would raise <c>42703</c>. What changed is only <i>which</i> delimiters are used - the connector's,
        /// rather than an ANSI double quote hardcoded here.
        /// </para>
        /// <para>
        /// <c>DEFAULT CURRENT_TIMESTAMP</c> is measured on all four providers rather than assumed: accepted by
        /// SQLite, PostgreSQL 16.15, MySQL 8.4.11 (on <c>DATETIME</c>) and SQL Server 2022 (on
        /// <c>DATETIME2</c>), so it needs no provider capability of its own.
        /// </para>
        /// <para>
        /// <c>internal</c> rather than <c>private</c> so the statement can be pinned per dialect without a
        /// server: this is a shared project, so its source compiles into each consuming assembly and the test
        /// project sees it directly. That matters because the MySQL half of the defect is a plain syntax
        /// error, and a syntax error deserves a guard that runs on a machine with no databases.
        /// </para>
        /// </remarks>
        internal string CreateMigrationsTableSql()
        {
            var columns = new[]
            {
                ColumnDefinition("Version", FieldType.Long, required: true, primary: true),
                ColumnDefinition("Name", FieldType.String, required: true, maxLength: 255),
                ColumnDefinition("Description", FieldType.String),
                ColumnDefinition("CreatedAt", FieldType.DateTime, required: true),
                ColumnDefinition("AppliedAt", FieldType.DateTime, required: true, defaultExpression: "CURRENT_TIMESTAMP"),
            };

            return $"CREATE TABLE {TableReference} ({string.Join(", ", columns)});";
        }

        /// <summary>
        /// One column of the migrations table: quoted name, dialect type from the connector, then the
        /// constraints. Built through <see cref="SchemaField.For"/> because a connector reads a column's width
        /// off the field's <i>runtime type</i> - <c>ConvertType</c> tests <c>field is CharField</c> before it
        /// will emit a length at all (TASK-264), so a hand-rolled <c>AbstractField</c> would have produced the
        /// unbounded type for <c>Name</c> and quietly lost its 255.
        /// </summary>
        private string ColumnDefinition(
            string name,
            FieldType type,
            bool required = false,
            bool primary = false,
            int? maxLength = null,
            string? defaultExpression = null)
        {
            var field = SchemaField.For(new FieldDescriptor
            {
                Name = name,
                Type = type,
                IsPrimary = primary,
                IsRequired = required,
                MaxLength = maxLength,
            });

            var definition = new StringBuilder();
            definition.Append(Quote(name)).Append(' ').Append(_connector.ConvertType(field.Type, field));

            // NOT NULL is emitted for the primary key too. It is implied everywhere except SQLite, whose
            // INTEGER PRIMARY KEY famously still admits a NULL; stating it makes the four dialects agree.
            if (required || primary)
            {
                definition.Append(" NOT NULL");
            }

            if (primary)
            {
                definition.Append(" PRIMARY KEY");
            }

            if (defaultExpression != null)
            {
                definition.Append(" DEFAULT ").Append(defaultExpression);
            }

            return definition.ToString();
        }

        /// <summary>
        /// The migrations table, quoted for this dialect. <c>QualifiedIdentifier</c> rather than
        /// <c>QuoteIdentifier</c> so a configured <c>Schema</c> is quoted per part, instead of asking for one
        /// object whose name literally contains a period (TASK-262).
        /// </summary>
        private string TableReference => _connector.QualifiedIdentifier(_settings.QualifiedTableName);

        /// <summary>A column identifier, quoted with this provider's own delimiters.</summary>
        private string Quote(string identifier) => _connector.QuoteIdentifier(identifier);

        private ISet<long> GetAppliedVersions(DbConnection connection)
        {
            var result = new HashSet<long>();

            if (!TableExists(connection))
            {
                return result;
            }

            using var command = connection.CreateCommand();
            command.CommandText = $"SELECT {Quote("Version")} FROM {TableReference}";

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
            command.CommandText = $"SELECT {Quote("Version")} FROM {TableReference}";

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
                INSERT INTO {TableReference}
                ({Quote("Version")}, {Quote("Name")}, {Quote("Description")}, {Quote("CreatedAt")})
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
                INSERT INTO {TableReference}
                ({Quote("Version")}, {Quote("Name")}, {Quote("Description")}, {Quote("CreatedAt")})
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
            command.CommandText = $"DELETE FROM {TableReference} WHERE {Quote("Version")} = @Version;";
            AddParameter(command, "@Version", migration.Version);
            command.ExecuteNonQuery();
        }

        private async Task RemoveMigrationAsync(DbConnection connection, DbTransaction transaction, Data.Migrations.IMigration migration, CancellationToken cancellationToken)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $"DELETE FROM {TableReference} WHERE {Quote("Version")} = @Version;";
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
