using System;

namespace Birko.Data.SQL.Connectors
{
    public partial class MSSqlConnector
    {
        /// <summary>
        /// The column names SQL Server rejects when Birko emits them (TASK-862). Measured live: an entity with an int column of
        /// each candidate name was created, inserted, read, filtered, counted, updated and deleted through the store.
        /// </summary>
        /// <remarks>
        /// The documented T-SQL reserved keywords that SQL Server 2022 (16.0.4295.3) rejected in <c>CREATE TABLE</c>:
        /// 179 of the 185. It accepted <c>DISK</c>, <c>DUMP</c>, <c>LOAD</c>, <c>PRECISION</c>, <c>SECURITYAUDIT</c>
        /// and <c>WITHIN</c>, so they are not refused. No ODBC or future keyword failed (<c>RANK</c> and <c>VALUE</c>
        /// work). <c>USER</c> matters beyond the syntax error: unquoted, it is the current user, so a write filtered
        /// on it compares the user's name, not the column.
        /// </remarks>
        internal static readonly RejectedColumnNames RejectedColumns = new RejectedColumnNames("SQL Server", "SQL Server 2022 (16.0.4295.3), the documented reserved keywords that fail", new[]
        {
            "ADD", "ALL", "ALTER", "AND", "ANY", "AS", "ASC", "AUTHORIZATION", "BACKUP", "BEGIN", "BETWEEN", "BREAK",
            "BROWSE", "BULK", "BY", "CASCADE", "CASE", "CHECK", "CHECKPOINT", "CLOSE", "CLUSTERED", "COALESCE",
            "COLLATE", "COLUMN", "COMMIT", "COMPUTE", "CONSTRAINT", "CONTAINS", "CONTAINSTABLE", "CONTINUE",
            "CONVERT", "CREATE", "CROSS", "CURRENT", "CURRENT_DATE", "CURRENT_TIME", "CURRENT_TIMESTAMP",
            "CURRENT_USER", "CURSOR", "DATABASE", "DBCC", "DEALLOCATE", "DECLARE", "DEFAULT", "DELETE", "DENY",
            "DESC", "DISTINCT", "DISTRIBUTED", "DOUBLE", "DROP", "ELSE", "END", "ERRLVL", "ESCAPE", "EXCEPT", "EXEC",
            "EXECUTE", "EXISTS", "EXIT", "EXTERNAL", "FETCH", "FILE", "FILLFACTOR", "FOR", "FOREIGN", "FREETEXT",
            "FREETEXTTABLE", "FROM", "FULL", "FUNCTION", "GOTO", "GRANT", "GROUP", "HAVING", "HOLDLOCK", "IDENTITY",
            "IDENTITYCOL", "IDENTITY_INSERT", "IF", "IN", "INDEX", "INNER", "INSERT", "INTERSECT", "INTO", "IS",
            "JOIN", "KEY", "KILL", "LEFT", "LIKE", "LINENO", "MERGE", "NATIONAL", "NOCHECK", "NONCLUSTERED", "NOT",
            "NULL", "NULLIF", "OF", "OFF", "OFFSETS", "ON", "OPEN", "OPENDATASOURCE", "OPENQUERY", "OPENROWSET",
            "OPENXML", "OPTION", "OR", "ORDER", "OUTER", "OVER", "PERCENT", "PIVOT", "PLAN", "PRIMARY", "PRINT",
            "PROC", "PROCEDURE", "PUBLIC", "RAISERROR", "READ", "READTEXT", "RECONFIGURE", "REFERENCES",
            "REPLICATION", "RESTORE", "RESTRICT", "RETURN", "REVERT", "REVOKE", "RIGHT", "ROLLBACK", "ROWCOUNT",
            "ROWGUIDCOL", "RULE", "SAVE", "SCHEMA", "SELECT", "SEMANTICKEYPHRASETABLE",
            "SEMANTICSIMILARITYDETAILSTABLE", "SEMANTICSIMILARITYTABLE", "SESSION_USER", "SET", "SETUSER",
            "SHUTDOWN", "SOME", "STATISTICS", "SYSTEM_USER", "TABLE", "TABLESAMPLE", "TEXTSIZE", "THEN", "TO", "TOP",
            "TRAN", "TRANSACTION", "TRIGGER", "TRUNCATE", "TRY_CONVERT", "TSEQUAL", "UNION", "UNIQUE", "UNPIVOT",
            "UPDATE", "UPDATETEXT", "USE", "USER", "VALUES", "VARYING", "VIEW", "WAITFOR", "WHEN", "WHERE", "WHILE",
            "WITH", "WRITETEXT"
        });

        /// <inheritdoc/>
        protected override RejectedColumnNames? ColumnNameRefusals => RejectedColumns;
    }
}
