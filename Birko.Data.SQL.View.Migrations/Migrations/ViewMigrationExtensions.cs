using Birko.Data.Migrations.SQL;
using System;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;

namespace Birko.Data.SQL.View.Migrations
{
    /// <summary>
    /// Extension methods on SqlMigration for creating and dropping SQL views in migrations.
    /// Generates DDL from view type metadata and executes it against the migration's connection.
    /// </summary>
    public static class ViewMigrationExtensions
    {
        /// <summary>
        /// Creates a SQL VIEW from a view type's metadata.
        /// </summary>
        /// <param name="migration">The SQL migration instance.</param>
        /// <param name="connection">The database connection.</param>
        /// <param name="transaction">The active transaction, or null if no transaction.</param>
        /// <param name="viewType">The type decorated with ViewAttribute(s) and ViewFieldAttribute(s).</param>
        /// <param name="viewName">Optional custom view name. If null, derives from type metadata.</param>
        /// <param name="quoteChar">Quote character for identifiers. Defaults to ANSI SQL double quote.</param>
        public static void CreateView(this SqlMigration migration, DbConnection connection, DbTransaction? transaction, Type viewType, string? viewName = null, char quoteChar = ViewSqlGenerator.DefaultQuoteChar)
        {
            if (migration == null)
            {
                throw new ArgumentNullException(nameof(migration));
            }

            if (connection == null)
            {
                throw new ArgumentNullException(nameof(connection));
            }

            if (viewType == null)
            {
                throw new ArgumentNullException(nameof(viewType));
            }

            var sql = ViewSqlGenerator.GenerateCreateViewSql(viewType, viewName, quoteChar);
            ExecuteSql(connection, transaction, sql);
        }

        /// <summary>
        /// Asynchronously creates a SQL VIEW from a view type's metadata.
        /// </summary>
        /// <param name="migration">The SQL migration instance.</param>
        /// <param name="connection">The database connection.</param>
        /// <param name="transaction">The active transaction, or null if no transaction.</param>
        /// <param name="viewType">The type decorated with ViewAttribute(s) and ViewFieldAttribute(s).</param>
        /// <param name="viewName">Optional custom view name. If null, derives from type metadata.</param>
        /// <param name="quoteChar">Quote character for identifiers. Defaults to ANSI SQL double quote.</param>
        /// <param name="ct">Cancellation token.</param>
        public static async Task CreateViewAsync(this SqlMigration migration, DbConnection connection, DbTransaction? transaction, Type viewType, string? viewName = null, char quoteChar = ViewSqlGenerator.DefaultQuoteChar, CancellationToken ct = default)
        {
            if (migration == null)
            {
                throw new ArgumentNullException(nameof(migration));
            }

            if (connection == null)
            {
                throw new ArgumentNullException(nameof(connection));
            }

            if (viewType == null)
            {
                throw new ArgumentNullException(nameof(viewType));
            }

            var sql = ViewSqlGenerator.GenerateCreateViewSql(viewType, viewName, quoteChar);
            await ExecuteSqlAsync(connection, transaction, sql, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Drops a SQL VIEW by view type.
        /// </summary>
        /// <param name="migration">The SQL migration instance.</param>
        /// <param name="connection">The database connection.</param>
        /// <param name="transaction">The active transaction, or null if no transaction.</param>
        /// <param name="viewType">The type decorated with ViewAttribute(s).</param>
        /// <param name="viewName">Optional custom view name. If null, derives from type metadata.</param>
        /// <param name="quoteChar">Quote character for identifiers. Defaults to ANSI SQL double quote.</param>
        public static void DropView(this SqlMigration migration, DbConnection connection, DbTransaction? transaction, Type viewType, string? viewName = null, char quoteChar = ViewSqlGenerator.DefaultQuoteChar)
        {
            if (migration == null)
            {
                throw new ArgumentNullException(nameof(migration));
            }

            if (connection == null)
            {
                throw new ArgumentNullException(nameof(connection));
            }

            if (viewType == null)
            {
                throw new ArgumentNullException(nameof(viewType));
            }

            var sql = ViewSqlGenerator.GenerateDropViewSql(viewType, viewName, quoteChar);
            ExecuteSql(connection, transaction, sql);
        }

        /// <summary>
        /// Asynchronously drops a SQL VIEW by view type.
        /// </summary>
        /// <param name="migration">The SQL migration instance.</param>
        /// <param name="connection">The database connection.</param>
        /// <param name="transaction">The active transaction, or null if no transaction.</param>
        /// <param name="viewType">The type decorated with ViewAttribute(s).</param>
        /// <param name="viewName">Optional custom view name. If null, derives from type metadata.</param>
        /// <param name="quoteChar">Quote character for identifiers. Defaults to ANSI SQL double quote.</param>
        /// <param name="ct">Cancellation token.</param>
        public static async Task DropViewAsync(this SqlMigration migration, DbConnection connection, DbTransaction? transaction, Type viewType, string? viewName = null, char quoteChar = ViewSqlGenerator.DefaultQuoteChar, CancellationToken ct = default)
        {
            if (migration == null)
            {
                throw new ArgumentNullException(nameof(migration));
            }

            if (connection == null)
            {
                throw new ArgumentNullException(nameof(connection));
            }

            if (viewType == null)
            {
                throw new ArgumentNullException(nameof(viewType));
            }

            var sql = ViewSqlGenerator.GenerateDropViewSql(viewType, viewName, quoteChar);
            await ExecuteSqlAsync(connection, transaction, sql, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Drops a SQL VIEW by name.
        /// </summary>
        /// <param name="migration">The SQL migration instance.</param>
        /// <param name="connection">The database connection.</param>
        /// <param name="transaction">The active transaction, or null if no transaction.</param>
        /// <param name="viewName">The name of the view to drop.</param>
        /// <param name="quoteChar">Quote character for identifiers. Defaults to ANSI SQL double quote.</param>
        public static void DropView(this SqlMigration migration, DbConnection connection, DbTransaction? transaction, string viewName, char quoteChar = ViewSqlGenerator.DefaultQuoteChar)
        {
            if (migration == null)
            {
                throw new ArgumentNullException(nameof(migration));
            }

            if (connection == null)
            {
                throw new ArgumentNullException(nameof(connection));
            }

            if (string.IsNullOrWhiteSpace(viewName))
            {
                throw new ArgumentException("View name cannot be null or empty.", nameof(viewName));
            }

            var sql = ViewSqlGenerator.GenerateDropViewSql(viewName, quoteChar);
            ExecuteSql(connection, transaction, sql);
        }

        /// <summary>
        /// Asynchronously drops a SQL VIEW by name.
        /// </summary>
        /// <param name="migration">The SQL migration instance.</param>
        /// <param name="connection">The database connection.</param>
        /// <param name="transaction">The active transaction, or null if no transaction.</param>
        /// <param name="viewName">The name of the view to drop.</param>
        /// <param name="quoteChar">Quote character for identifiers. Defaults to ANSI SQL double quote.</param>
        /// <param name="ct">Cancellation token.</param>
        public static async Task DropViewAsync(this SqlMigration migration, DbConnection connection, DbTransaction? transaction, string viewName, char quoteChar = ViewSqlGenerator.DefaultQuoteChar, CancellationToken ct = default)
        {
            if (migration == null)
            {
                throw new ArgumentNullException(nameof(migration));
            }

            if (connection == null)
            {
                throw new ArgumentNullException(nameof(connection));
            }

            if (string.IsNullOrWhiteSpace(viewName))
            {
                throw new ArgumentException("View name cannot be null or empty.", nameof(viewName));
            }

            var sql = ViewSqlGenerator.GenerateDropViewSql(viewName, quoteChar);
            await ExecuteSqlAsync(connection, transaction, sql, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Executes a SQL statement synchronously against a connection.
        /// </summary>
        private static void ExecuteSql(DbConnection connection, DbTransaction? transaction, string sql)
        {
            if (string.IsNullOrWhiteSpace(sql))
            {
                return;
            }

            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = sql;
            command.ExecuteNonQuery();
        }

        /// <summary>
        /// Executes a SQL statement asynchronously against a connection.
        /// </summary>
        private static async Task ExecuteSqlAsync(DbConnection connection, DbTransaction? transaction, string sql, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(sql))
            {
                return;
            }

            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = sql;
            await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
    }
}
