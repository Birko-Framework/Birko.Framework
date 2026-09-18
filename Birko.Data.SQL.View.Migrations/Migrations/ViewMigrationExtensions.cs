using Birko.Data.Migrations.Context;
using Birko.Data.Migrations.SQL.Context;
using System;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;

namespace Birko.Data.SQL.View.Migrations
{
    /// <summary>
    /// Extension methods on IMigrationContext for creating and dropping SQL views in migrations.
    /// Generates DDL from view type metadata and executes it against the context's connection.
    /// </summary>
    public static class ViewMigrationExtensions
    {
        /// <summary>
        /// Creates a SQL VIEW from a view type's metadata.
        /// </summary>
        /// <param name="context">The SQL migration context.</param>
        /// <param name="viewType">The type decorated with ViewAttribute(s) and ViewFieldAttribute(s).</param>
        /// <param name="viewName">Optional custom view name. If null, derives from type metadata.</param>
        /// <param name="quoteChar">Quote character for identifiers. Defaults to ANSI SQL double quote.</param>
        public static void CreateView(this IMigrationContext context, Type viewType, string? viewName = null, char quoteChar = ViewSqlGenerator.DefaultQuoteChar)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            if (viewType == null)
            {
                throw new ArgumentNullException(nameof(viewType));
            }

            var sql = ViewSqlGenerator.GenerateCreateViewSql(viewType, viewName, quoteChar);
            var (connection, transaction) = GetSqlConnection(context);
            ExecuteSql(connection, transaction, sql);
        }

        /// <summary>
        /// Asynchronously creates a SQL VIEW from a view type's metadata.
        /// </summary>
        /// <param name="context">The SQL migration context.</param>
        /// <param name="viewType">The type decorated with ViewAttribute(s) and ViewFieldAttribute(s).</param>
        /// <param name="viewName">Optional custom view name. If null, derives from type metadata.</param>
        /// <param name="quoteChar">Quote character for identifiers. Defaults to ANSI SQL double quote.</param>
        /// <param name="ct">Cancellation token.</param>
        public static async Task CreateViewAsync(this IMigrationContext context, Type viewType, string? viewName = null, char quoteChar = ViewSqlGenerator.DefaultQuoteChar, CancellationToken ct = default)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            if (viewType == null)
            {
                throw new ArgumentNullException(nameof(viewType));
            }

            var sql = ViewSqlGenerator.GenerateCreateViewSql(viewType, viewName, quoteChar);
            var (connection, transaction) = GetSqlConnection(context);
            await ExecuteSqlAsync(connection, transaction, sql, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Drops a SQL VIEW by view type.
        /// </summary>
        /// <param name="context">The SQL migration context.</param>
        /// <param name="viewType">The type decorated with ViewAttribute(s).</param>
        /// <param name="viewName">Optional custom view name. If null, derives from type metadata.</param>
        /// <param name="quoteChar">Quote character for identifiers. Defaults to ANSI SQL double quote.</param>
        public static void DropView(this IMigrationContext context, Type viewType, string? viewName = null, char quoteChar = ViewSqlGenerator.DefaultQuoteChar)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            if (viewType == null)
            {
                throw new ArgumentNullException(nameof(viewType));
            }

            var sql = ViewSqlGenerator.GenerateDropViewSql(viewType, viewName, quoteChar);
            var (connection, transaction) = GetSqlConnection(context);
            ExecuteSql(connection, transaction, sql);
        }

        /// <summary>
        /// Asynchronously drops a SQL VIEW by view type.
        /// </summary>
        /// <param name="context">The SQL migration context.</param>
        /// <param name="viewType">The type decorated with ViewAttribute(s).</param>
        /// <param name="viewName">Optional custom view name. If null, derives from type metadata.</param>
        /// <param name="quoteChar">Quote character for identifiers. Defaults to ANSI SQL double quote.</param>
        /// <param name="ct">Cancellation token.</param>
        public static async Task DropViewAsync(this IMigrationContext context, Type viewType, string? viewName = null, char quoteChar = ViewSqlGenerator.DefaultQuoteChar, CancellationToken ct = default)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            if (viewType == null)
            {
                throw new ArgumentNullException(nameof(viewType));
            }

            var sql = ViewSqlGenerator.GenerateDropViewSql(viewType, viewName, quoteChar);
            var (connection, transaction) = GetSqlConnection(context);
            await ExecuteSqlAsync(connection, transaction, sql, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Drops a SQL VIEW by name.
        /// </summary>
        /// <param name="context">The SQL migration context.</param>
        /// <param name="viewName">The name of the view to drop.</param>
        /// <param name="quoteChar">Quote character for identifiers. Defaults to ANSI SQL double quote.</param>
        public static void DropView(this IMigrationContext context, string viewName, char quoteChar = ViewSqlGenerator.DefaultQuoteChar)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            if (string.IsNullOrWhiteSpace(viewName))
            {
                throw new ArgumentException("View name cannot be null or empty.", nameof(viewName));
            }

            var sql = ViewSqlGenerator.GenerateDropViewSql(viewName, quoteChar);
            var (connection, transaction) = GetSqlConnection(context);
            ExecuteSql(connection, transaction, sql);
        }

        /// <summary>
        /// Asynchronously drops a SQL VIEW by name.
        /// </summary>
        /// <param name="context">The SQL migration context.</param>
        /// <param name="viewName">The name of the view to drop.</param>
        /// <param name="quoteChar">Quote character for identifiers. Defaults to ANSI SQL double quote.</param>
        /// <param name="ct">Cancellation token.</param>
        public static async Task DropViewAsync(this IMigrationContext context, string viewName, char quoteChar = ViewSqlGenerator.DefaultQuoteChar, CancellationToken ct = default)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            if (string.IsNullOrWhiteSpace(viewName))
            {
                throw new ArgumentException("View name cannot be null or empty.", nameof(viewName));
            }

            var sql = ViewSqlGenerator.GenerateDropViewSql(viewName, quoteChar);
            var (connection, transaction) = GetSqlConnection(context);
            await ExecuteSqlAsync(connection, transaction, sql, ct).ConfigureAwait(false);
        }

        private static (DbConnection connection, DbTransaction? transaction) GetSqlConnection(IMigrationContext context)
        {
            if (context is SqlMigrationContext sqlContext)
            {
                return (sqlContext.Connection, sqlContext.Transaction);
            }

            throw new InvalidOperationException($"Expected SqlMigrationContext but got {context.GetType().Name}.");
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
