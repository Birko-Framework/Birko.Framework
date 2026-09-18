using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.IndexManagement;
using System.Linq;

namespace Birko.Data.SQL.MSSql.IndexManagement
{
    /// <summary>
    /// MSSQL dialect for <see cref="SqlIndexManager"/>.
    /// Uses sys.indexes / sys.index_columns catalog views.
    /// </summary>
    public class MSSqlIndexManager : SqlIndexManager
    {
        public MSSqlIndexManager(AbstractConnectorBase connector) : base(connector)
        {
        }

        protected override string IndexExistsSql(string tableName, string indexName)
        {
            var safeIndex = SqlLiteral.EscapeLiteral(indexName);
            var safeTable = SqlLiteral.EscapeLiteral(tableName);
            return $"SELECT COUNT(*) FROM sys.indexes WHERE name = '{safeIndex}' AND object_id = OBJECT_ID('{safeTable}')";
        }

        protected override string ListIndexesSql(string tableName)
        {
            var safeTable = SqlLiteral.EscapeLiteral(tableName);
            return $@"SELECT
    i.name AS index_name,
    COL_NAME(ic.object_id, ic.column_id) AS column_name,
    CASE WHEN ic.is_descending_key = 1 THEN 1 ELSE 0 END AS is_descending,
    CASE WHEN i.is_unique = 1 THEN 1 ELSE 0 END AS is_unique,
    ic.key_ordinal AS ordinal_position
FROM sys.indexes i
JOIN sys.index_columns ic ON i.object_id = ic.object_id AND i.index_id = ic.index_id
WHERE i.object_id = OBJECT_ID('{safeTable}')
  AND i.is_primary_key = 0
  AND i.type > 0
  AND ic.is_included_column = 0
ORDER BY i.name, ic.key_ordinal";
        }

        // TASK-245 removed CreateUniqueIndexSql: it was byte-identical to what
        // MSSqlConnector.CreateIndexSql already emits for an index whose Unique flag is set, and the
        // duplicate existed only because SqlIndexManager.ToSqlIndexDefinition never copied that flag.
        // The connector emitter is now the single producer of index DDL for every dialect.
    }
}
