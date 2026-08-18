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
        /// <summary>
        /// Creates the TimescaleDB migration context. <paramref name="connector"/> is <b>required</b>.
        /// </summary>
        /// <remarks>
        /// It was <c>AbstractConnector? connector = null</c>, which had been wrong since TASK-247 made the
        /// base's connector required — the optional argument still advertised a connector-free context the
        /// base could not build, and passing it produced CS8604 at the base call. TASK-253 made it
        /// load-bearing rather than merely untidy: <c>TimescaleDBMigration</c>'s emitters now resolve their
        /// identifiers through <c>SqlMigrationContext.Connector</c>, so a null would surface as a
        /// <see cref="System.NullReferenceException"/> from inside a DDL builder rather than at construction.
        /// The only caller, <c>TimescaleDBMigrationRunner.ExecuteSingleMigration</c>, always had one to pass.
        /// </remarks>
        public TimescaleDBMigrationContext(DbConnection connection, DbTransaction? transaction, AbstractConnector connector)
            : base(connection, transaction, "TimescaleDB", connector)
        {
        }
    }
}
