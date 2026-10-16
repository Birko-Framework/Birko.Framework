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
            var safeIndex = SqlLiteral.EscapeLiteral(indexName);
            // SH-M227 (TASK-154): pg_indexes spans every schema, so a same-named table elsewhere answered for this one.
            // TASK-272: the table is the relation to_regclass resolves: the declared schema, else the search path.
            return $@"SELECT COUNT(*) FROM pg_index x
JOIN pg_class t ON t.oid = x.indrelid
JOIN pg_class i ON i.oid = x.indexrelid
WHERE t.oid = to_regclass('{Connector.RelationLiteral(tableName)}') AND i.relname = '{safeIndex}'";
        }

        /// <remarks>
        /// SH-M232 (TASK-154): <c>indoption</c> is ordered by position in the INDEX, and was subscripted by the column's
        /// position in the TABLE (<c>attnum - 1</c>), so a DESC index on a later column read ascending. Both int2vectors
        /// are zero-based, so the column's position in <c>indkey</c> is the subscript (measured live). Scoped to the
        /// visible table (SH-M227).
        /// </remarks>
        protected override string ListIndexesSql(string tableName)
        {
            var relation = Connector.RelationLiteral(tableName);
            return $@"SELECT
    i.relname AS index_name,
    a.attname AS column_name,
    CASE WHEN pg_index.indoption[array_position(pg_index.indkey, a.attnum)] & 1 = 1 THEN 1 ELSE 0 END AS is_descending,
    CASE WHEN pg_index.indisunique THEN 1 ELSE 0 END AS is_unique,
    array_position(pg_index.indkey, a.attnum) AS ordinal_position
FROM pg_class t
JOIN pg_index ON t.oid = pg_index.indrelid
JOIN pg_class i ON i.oid = pg_index.indexrelid
JOIN pg_attribute a ON a.attrelid = t.oid AND a.attnum = ANY(pg_index.indkey)
WHERE t.oid = to_regclass('{relation}')
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
