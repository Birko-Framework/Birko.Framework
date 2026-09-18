using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.IndexManagement;

namespace Birko.Data.SQL.MySQL.IndexManagement
{
    /// <summary>
    /// MySQL dialect for <see cref="SqlIndexManager"/>.
    /// Uses information_schema.statistics (the default base implementation).
    /// </summary>
    public class MySqlIndexManager : SqlIndexManager
    {
        public MySqlIndexManager(AbstractConnectorBase connector) : base(connector)
        {
        }

        // MySQL uses information_schema.statistics for the LOOKUP queries, so the base implementations of
        // IndexExistsSql / ListIndexesSql are correct here.
        //
        // TASK-245: the previous wording — "the base implementation is already correct" — was unqualified,
        // and that is part of why this shipped. It was NOT correct for index CREATION: the base emitted
        // CREATE INDEX IF NOT EXISTS, which MySQL rejects outright (ERROR 1064), so no declared index on a
        // MySQL entity was ever built. That is fixed on the connector (MySQLConnector.CreateIndexSql), which
        // is the single producer of index DDL for every dialect, so this class deliberately overrides
        // nothing for creation. Note IIndexManager.CreateAsync executes through its own ExecuteNonQueryAsync
        // rather than the connector's CreateIndexes funnel, so it does not get the 1061 "already there"
        // tolerance — correct for an explicit, bare-metal call whose failure is wrapped in
        // IndexManagementException and whose callers have ExistsAsync.
    }
}
