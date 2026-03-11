using System;
using System.Data;
using System.Data.Common;

namespace Birko.Data.Migrations.SQL
{
    /// <summary>
    /// Abstract base class for SQL-based migrations.
    /// Provides access to the database connection for executing SQL scripts.
    /// </summary>
    public abstract class SqlMigration : Data.Migrations.AbstractMigration
    {
        /// <summary>
        /// Executes the migration SQL.
        /// Override this method to provide custom migration logic.
        /// </summary>
        /// <param name="connection">The database connection.</param>
        /// <param name="transaction">The active transaction, or null if no transaction.</param>
        /// <param name="direction">The migration direction.</param>
        protected abstract void ExecuteSql(DbConnection connection, DbTransaction? transaction, Data.Migrations.MigrationDirection direction);

        /// <summary>
        /// Gets the SQL to execute for the Up migration.
        /// Override this OR ExecuteSql to provide migration logic.
        /// </summary>
        protected virtual string UpSql => string.Empty;

        /// <summary>
        /// Gets the SQL to execute for the Down migration.
        /// Override this OR ExecuteSql to provide migration logic.
        /// </summary>
        protected virtual string DownSql => string.Empty;

        /// <summary>
        /// Applies the migration (upgrade).
        /// </summary>
        public override void Up()
        {
            throw new InvalidOperationException("SqlMigration requires a database connection. Use SqlMigrationRunner to execute migrations.");
        }

        /// <summary>
        /// Reverts the migration (downgrade).
        /// </summary>
        public override void Down()
        {
            throw new InvalidOperationException("SqlMigration requires a database connection. Use SqlMigrationRunner to execute migrations.");
        }

        /// <summary>
        /// Internal execution method called by SqlMigrationRunner.
        /// </summary>
        internal void Execute(DbConnection connection, DbTransaction? transaction, Data.Migrations.MigrationDirection direction)
        {
            if (!string.IsNullOrEmpty(UpSql) && direction == Data.Migrations.MigrationDirection.Up)
            {
                ExecuteScript(connection, transaction, UpSql);
            }
            else if (!string.IsNullOrEmpty(DownSql) && direction == Data.Migrations.MigrationDirection.Down)
            {
                ExecuteScript(connection, transaction, DownSql);
            }
            else
            {
                ExecuteSql(connection, transaction, direction);
            }
        }

        /// <summary>
        /// Executes a SQL script.
        /// Handles batched scripts separated by semicolons.
        /// </summary>
        /// <param name="connection">The database connection.</param>
        /// <param name="transaction">The active transaction, or null if no transaction.</param>
        /// <param name="sql">The SQL script to execute.</param>
        protected virtual void ExecuteScript(DbConnection connection, DbTransaction? transaction, string sql)
        {
            if (string.IsNullOrWhiteSpace(sql))
                return;

            // Split by semicolon, but ignore those inside quotes or parentheses
            var statements = SplitSqlStatements(sql);

            foreach (var statement in statements)
            {
                if (string.IsNullOrWhiteSpace(statement))
                    continue;

                using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = statement;
                command.ExecuteNonQuery();
            }
        }

        /// <summary>
        /// Splits a SQL script into individual statements.
        /// </summary>
        private System.Collections.Generic.IEnumerable<string> SplitSqlStatements(string sql)
        {
            var statements = new System.Collections.Generic.List<string>();
            var current = new System.Text.StringBuilder();
            var inQuote = false;
            var quoteChar = '\0';
            var inParentheses = 0;

            foreach (var ch in sql)
            {
                if ((ch == '\'' || ch == '"') && (inQuote && quoteChar == ch))
                {
                    inQuote = false;
                    quoteChar = '\0';
                }
                else if ((ch == '\'' || ch == '"') && !inQuote)
                {
                    inQuote = true;
                    quoteChar = ch;
                }
                else if (ch == '(' && !inQuote)
                {
                    inParentheses++;
                }
                else if (ch == ')' && !inQuote && inParentheses > 0)
                {
                    inParentheses--;
                }
                else if (ch == ';' && !inQuote && inParentheses == 0)
                {
                    var statement = current.ToString().Trim();
                    if (!string.IsNullOrEmpty(statement))
                    {
                        statements.Add(statement);
                    }
                    current.Clear();
                    continue;
                }

                current.Append(ch);
            }

            var remaining = current.ToString().Trim();
            if (!string.IsNullOrEmpty(remaining))
            {
                statements.Add(remaining);
            }

            return statements;
        }

        /// <summary>
        /// Creates a parameter for a command.
        /// </summary>
        /// <param name="command">The command to add the parameter to.</param>
        /// <param name="name">The parameter name.</param>
        /// <param name="value">The parameter value.</param>
        protected static DbParameter AddParameter(DbCommand command, string name, object? value)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = name;
            parameter.Value = value ?? DBNull.Value;
            command.Parameters.Add(parameter);
            return parameter;
        }

        /// <summary>
        /// Checks if a table exists in the database.
        /// </summary>
        /// <param name="connection">The database connection.</param>
        /// <param name="tableName">The table name to check.</param>
        /// <returns>True if the table exists; otherwise, false.</returns>
        protected bool TableExists(DbConnection connection, string tableName)
        {
            var schema = connection.GetSchema("Tables");
            foreach (DataRow row in schema.Rows)
            {
                var currentTableName = row["TABLE_NAME"] as string;
                if (string.Equals(currentTableName, tableName, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Checks if a column exists in a table.
        /// </summary>
        /// <param name="connection">The database connection.</param>
        /// <param name="tableName">The table name.</param>
        /// <param name="columnName">The column name.</param>
        /// <returns>True if the column exists; otherwise, false.</returns>
        protected bool ColumnExists(DbConnection connection, string tableName, string columnName)
        {
            var schema = connection.GetSchema("Columns");
            foreach (DataRow row in schema.Rows)
            {
                var currentTableName = row["TABLE_NAME"] as string;
                var currentColumnName = row["COLUMN_NAME"] as string;
                if (string.Equals(currentTableName, tableName, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(currentColumnName, columnName, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }
    }
}
