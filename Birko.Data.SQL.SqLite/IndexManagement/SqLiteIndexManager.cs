using Birko.Data.Patterns.IndexManagement;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.IndexManagement;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Birko.Data.SQL.SQLite.IndexManagement
{
    /// <summary>
    /// SQLite dialect for <see cref="SqlIndexManager"/>.
    /// Uses sqlite_master and PRAGMA index_info for listing.
    /// </summary>
    public class SqLiteIndexManager : SqlIndexManager
    {
        public SqLiteIndexManager(AbstractConnectorBase connector) : base(connector)
        {
        }

        protected override string IndexExistsSql(string tableName, string indexName)
        {
            // SH-M225 (TASK-154): the table was accepted and never used, so an index of the same name on another
            // table answered "exists" and a guarded CREATE INDEX was skipped.
            var safeIndex = SqlLiteral.EscapeLiteral(indexName);
            var safeTable = SqlLiteral.EscapeLiteral(Connector.PhysicalTable(tableName).Name);
            return $"SELECT COUNT(*) FROM sqlite_master WHERE type = 'index' AND name = '{safeIndex}' AND tbl_name = '{safeTable}'";
        }

        /// <summary>
        /// Lists the table's declared indexes from SQLite's own index metadata (TASK-154), through the base reader.
        /// Uniqueness used to be <c>sql LIKE '%UNIQUE%'</c> (SH-M226), so an index merely NAMED <c>IX_UNIQUE_CODE</c>
        /// reported unique, and every column read ascending (SH-M244), so a desired-against-actual comparison never
        /// settled. <c>pragma_index_list</c> carries the real unique flag and the origin (<c>c</c> = CREATE INDEX, not a
        /// UNIQUE or PRIMARY KEY constraint); <c>pragma_index_xinfo</c> each key column's direction and position.
        /// </summary>
        protected override string ListIndexesSql(string tableName)
        {
            var safeTable = SqlLiteral.EscapeLiteral(Connector.PhysicalTable(tableName).Name);
            return $@"SELECT il.name, ii.name, ii.""desc"", il.""unique"", ii.seqno
FROM pragma_index_list('{safeTable}') AS il
JOIN pragma_index_xinfo(il.name) AS ii
WHERE il.origin = 'c' AND ii.""key"" = 1 AND ii.name IS NOT NULL
ORDER BY il.name, ii.seqno";
        }
    }
}
