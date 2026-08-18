using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.IndexManagement;
using System.Linq;

namespace Birko.Data.SQL.PostgreSQL.IndexManagement
{
    /// <summary>
    /// PostgreSQL dialect for <see cref="SqlIndexManager"/>.
    /// Uses pg_indexes catalog view for listing.
    /// </summary>
    public class PostgreSqlIndexManager : SqlIndexManager
    {
        public PostgreSqlIndexManager(AbstractConnectorBase connector) : base(connector)
        {
        }

        protected override string IndexExistsSql(string tableName, string indexName)
        {
            var safeIndex = indexName.Replace("'", "''");
            var safeTable = tableName.Replace("'", "''");
            return $"SELECT COUNT(*) FROM pg_indexes WHERE tablename = '{safeTable}' AND indexname = '{safeIndex}'";
        }

        protected override string ListIndexesSql(string tableName)
        {
            var safeTable = tableName.Replace("'", "''");
            return $@"SELECT
    i.relname AS index_name,
    a.attname AS column_name,
    CASE WHEN pg_index.indoption[a.attnum - 1] & 1 = 1 THEN 1 ELSE 0 END AS is_descending,
    CASE WHEN pg_index.indisunique THEN 1 ELSE 0 END AS is_unique,
    array_position(pg_index.indkey, a.attnum) AS ordinal_position
FROM pg_class t
JOIN pg_index ON t.oid = pg_index.indrelid
JOIN pg_class i ON i.oid = pg_index.indexrelid
JOIN pg_attribute a ON a.attrelid = t.oid AND a.attnum = ANY(pg_index.indkey)
WHERE t.relname = '{safeTable}'
  AND NOT pg_index.indisprimary
ORDER BY i.relname, array_position(pg_index.indkey, a.attnum)";
        }

        // TASK-245 removed CreateUniqueIndexSql. It quoted its column identifiers, which on PostgreSQL
        // cannot resolve the case-folded columns that bare-column CREATE TABLE actually creates — measured
        // as ERROR 42703 — so this emitter could never build a unique index on a PascalCase entity. Deleting
        // it routes through AbstractConnectorBase.CreateIndexSql, which emits columns bare, and makes the
        // connector the single producer of index DDL.
    }
}
