using System;

namespace Birko.Data.SQL.Connectors
{
    public partial class SqLiteConnector
    {
        /// <summary>
        /// The column names SQLite rejects when Birko emits them (TASK-862). Measured live: an entity with an int column of
        /// each candidate name was created, inserted, read, filtered, counted, updated and deleted through the store.
        /// </summary>
        /// <remarks>
        /// SQLite 3.53.3 accepts most of its keywords as column names. Refused: the 58 that failed
        /// <c>CREATE TABLE</c>; <c>CAST</c> and <c>RAISE</c>, which pass the DDL but fail a filtered update or delete;
        /// and <c>CURRENT_DATE</c>, <c>CURRENT_TIME</c> and <c>CURRENT_TIMESTAMP</c>, which pass the DDL and the reads
        /// but are read as the current date and time in a write's WHERE, so a filtered update or delete matched no rows,
        /// silently. <c>KEY</c>, <c>VALUE</c>, <c>USER</c>, <c>DESC</c> and <c>RANK</c> work.
        /// </remarks>
        internal static readonly RejectedColumnNames RejectedColumns = new RejectedColumnNames("SQLite", "SQLite 3.53.3 (Microsoft.Data.Sqlite 10), each measured", new[]
        {
            "ADD", "ALL", "ALTER", "AND", "AS", "AUTOINCREMENT", "BETWEEN", "CASE", "CAST", "CHECK", "COLLATE",
            "COMMIT", "CONSTRAINT", "CREATE", "CURRENT_DATE", "CURRENT_TIME", "CURRENT_TIMESTAMP", "DEFAULT",
            "DEFERRABLE", "DELETE", "DISTINCT", "DROP", "ELSE", "ESCAPE", "EXCEPT", "EXISTS", "FOREIGN", "FROM",
            "GROUP", "HAVING", "IN", "INDEX", "INSERT", "INTERSECT", "INTO", "IS", "ISNULL", "JOIN", "LIMIT", "NOT",
            "NOTHING", "NOTNULL", "NULL", "ON", "OR", "ORDER", "PRIMARY", "RAISE", "REFERENCES", "RETURNING",
            "SELECT", "SET", "TABLE", "THEN", "TO", "TRANSACTION", "UNION", "UNIQUE", "UPDATE", "USING", "VALUES",
            "WHEN", "WHERE"
        });

        /// <inheritdoc/>
        protected override RejectedColumnNames? ColumnNameRefusals => RejectedColumns;
    }
}
