using System;
using System.Data.Common;

namespace Birko.Data.Migrations.TimescaleDB
{
    /// <summary>
    /// Executes TimescaleDB migrations using PostgreSQL connection.
    /// </summary>
    public class TimescaleDBMigrationRunner : SQL.SqlMigrationRunner
    {
        /// <summary>
        /// Initializes a new instance of the TimescaleDBMigrationRunner class.
        /// </summary>
        /// <param name="connectionFactory">Factory function to create PostgreSQL connections.</param>
        /// <param name="settings">Migration settings (can use SqlMigrationSettings).</param>
        public TimescaleDBMigrationRunner(Func<DbConnection> connectionFactory, SQL.Settings.SqlMigrationSettings? settings = null)
            : base(connectionFactory, settings)
        {
        }
    }
}
