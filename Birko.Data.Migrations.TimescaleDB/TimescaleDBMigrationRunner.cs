using System;
using System.Data.Common;
using Birko.Data.Migrations.TimescaleDB.Context;
using Birko.Data.SQL.Connectors;

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
        /// <param name="connector">PostgreSQL connector from the store.</param>
        /// <param name="settings">Migration settings.</param>
        public TimescaleDBMigrationRunner(AbstractConnector connector, SQL.Settings.SqlMigrationSettings? settings = null)
            : base(connector, settings)
        {
        }

        /// <summary>
        /// Overrides to provide a TimescaleDB-specific context instead of the base SQL context.
        /// </summary>
        protected override void ExecuteSingleMigration(
            Data.Migrations.IMigration migration,
            Data.Migrations.MigrationDirection direction,
            DbConnection connection,
            DbTransaction? transaction)
        {
            var context = new TimescaleDBMigrationContext(connection, transaction);
            if (direction == Data.Migrations.MigrationDirection.Up)
                migration.Up(context);
            else
                migration.Down(context);
        }
    }
}
