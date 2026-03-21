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
            var safeIndex = indexName.Replace("'", "''");
            var safeTable = tableName.Replace("'", "''");
            return $"SELECT COUNT(*) FROM sys.indexes WHERE name = '{safeIndex}' AND object_id = OBJECT_ID('{safeTable}')";
        }

        protected override string ListIndexesSql(string tableName)
        {
            var safeTable = tableName.Replace("'", "''");
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

        protected override string CreateUniqueIndexSql(string tableName, Tables.IndexDefinition index)
        {
            var columns = string.Join(", ", index.Columns.Select(c =>
                Connector.QuoteIdentifier(c.ColumnName) + (c.IsDescending ? " DESC" : "")));

            var safeIndex = index.Name.Replace("'", "''");
            var safeTable = tableName.Replace("'", "''");
            return $"IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name='{safeIndex}' AND object_id=OBJECT_ID('{safeTable}')) "
                 + $"CREATE UNIQUE INDEX {Connector.QuoteIdentifier(index.Name)} ON {Connector.QuoteIdentifier(tableName)} ({columns})";
        }
    }
}
