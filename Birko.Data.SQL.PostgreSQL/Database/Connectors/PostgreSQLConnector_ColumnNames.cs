using System;

namespace Birko.Data.SQL.Connectors
{
    public partial class PostgreSQLConnector
    {
        /// <summary>
        /// The column names PostgreSQL rejects when Birko emits them (TASK-862). Measured live: an entity with an int column of
        /// each candidate name was created, inserted, read, filtered, counted, updated and deleted through the store.
        /// </summary>
        /// <remarks>
        /// The 101 words PostgreSQL 16.15 lists in <c>pg_get_keywords()</c> with catcode <c>R</c> (reserved) or
        /// <c>T</c> (reserved, can be a function or type name), each of which failed <c>CREATE TABLE</c>, plus the six
        /// system column names (<c>ctid</c>, <c>xmin</c>, <c>xmax</c>, <c>cmin</c>, <c>cmax</c>, <c>tableoid</c>), which
        /// fail with 42701. No <c>C</c> or <c>U</c> keyword failed any operation (<c>RANK</c>, <c>KEY</c>,
        /// <c>VALUE</c> and <c>INDEX</c> work). TimescaleDB inherits this list.
        /// </remarks>
        internal static readonly RejectedColumnNames RejectedColumns = new RejectedColumnNames("PostgreSQL", "PostgreSQL 16.15, pg_get_keywords() catcode R and T, plus system columns", new[]
        {
            "ALL", "ANALYSE", "ANALYZE", "AND", "ANY", "ARRAY", "AS", "ASC", "ASYMMETRIC", "AUTHORIZATION", "BINARY",
            "BOTH", "CASE", "CAST", "CHECK", "CMAX", "CMIN", "COLLATE", "COLLATION", "COLUMN", "CONCURRENTLY",
            "CONSTRAINT", "CREATE", "CROSS", "CTID", "CURRENT_CATALOG", "CURRENT_DATE", "CURRENT_ROLE",
            "CURRENT_SCHEMA", "CURRENT_TIME", "CURRENT_TIMESTAMP", "CURRENT_USER", "DEFAULT", "DEFERRABLE", "DESC",
            "DISTINCT", "DO", "ELSE", "END", "EXCEPT", "FALSE", "FETCH", "FOR", "FOREIGN", "FREEZE", "FROM", "FULL",
            "GRANT", "GROUP", "HAVING", "ILIKE", "IN", "INITIALLY", "INNER", "INTERSECT", "INTO", "IS", "ISNULL",
            "JOIN", "LATERAL", "LEADING", "LEFT", "LIKE", "LIMIT", "LOCALTIME", "LOCALTIMESTAMP", "NATURAL", "NOT",
            "NOTNULL", "NULL", "OFFSET", "ON", "ONLY", "OR", "ORDER", "OUTER", "OVERLAPS", "PLACING", "PRIMARY",
            "REFERENCES", "RETURNING", "RIGHT", "SELECT", "SESSION_USER", "SIMILAR", "SOME", "SYMMETRIC",
            "SYSTEM_USER", "TABLE", "TABLEOID", "TABLESAMPLE", "THEN", "TO", "TRAILING", "TRUE", "UNION", "UNIQUE",
            "USER", "USING", "VARIADIC", "VERBOSE", "WHEN", "WHERE", "WINDOW", "WITH", "XMAX", "XMIN"
        });

        /// <inheritdoc/>
        protected override RejectedColumnNames? ColumnNameRefusals => RejectedColumns;
    }
}
