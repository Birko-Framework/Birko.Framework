using System.Data.Common;
using Birko.Data.Migrations.SQL.Context;
using Birko.Data.SQL.Connectors;

namespace Birko.Data.Migrations.TimescaleDB.Context
{
    /// <summary>
    /// Migration context for TimescaleDB.
    /// Extends SQL migration context with TimescaleDB-specific provider name.
    /// </summary>
    public class TimescaleDBMigrationContext : SqlMigrationContext
    {
        public TimescaleDBMigrationContext(DbConnection connection, DbTransaction? transaction, AbstractConnector? connector = null)
            : base(connection, transaction, "TimescaleDB", connector)
        {
        }
    }
}
