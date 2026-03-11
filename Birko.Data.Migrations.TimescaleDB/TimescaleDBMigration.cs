using System;
using System.Data;
using System.Data.Common;

namespace Birko.Data.Migrations.TimescaleDB
{
    /// <summary>
    /// Abstract base class for TimescaleDB migrations.
    /// Extends SQL migrations with TimescaleDB-specific features like hypertables.
    /// </summary>
    public abstract class TimescaleDBMigration : SQL.SqlMigration
    {
        /// <summary>
        /// Executes the migration using the database connection.
        /// Override this method to provide custom migration logic.
        /// </summary>
        protected override void ExecuteSql(DbConnection connection, DbTransaction? transaction, Data.Migrations.MigrationDirection direction)
        {
            // Ensure TimescaleDB extension is loaded
            EnsureTimescaleDBExtension(connection, transaction);
        }

        /// <summary>
        /// Creates a hypertable from a regular table.
        /// </summary>
        /// <param name="connection">The database connection.</param>
        /// <param name="transaction">The active transaction, or null if no transaction.</param>
        /// <param name="tableName">The name of the table to convert.</param>
        /// <param name="timeColumnName">The name of the time column for partitioning.</param>
        /// <param name="chunkInterval">Optional chunk interval (e.g., "1 day", "1 hour").</param>
        protected virtual void CreateHypertable(DbConnection connection, DbTransaction? transaction, string tableName, string timeColumnName, string? chunkInterval = null)
        {
            var chunkIntervalSql = string.IsNullOrEmpty(chunkInterval) ? "" : $", chunk_time_interval => interval '{chunkInterval}'";

            var sql = $@"
                SELECT create_hypertable('{tableName}', '{timeColumnName}'{chunkIntervalSql});
            ";

            ExecuteScript(connection, transaction, sql);
        }

        /// <summary>
        /// Creates a hypertable with both time and space partitioning.
        /// </summary>
        protected virtual void CreateHypertableWithSpace(DbConnection connection, DbTransaction? transaction, string tableName, string timeColumnName, string spaceColumnName, int numberPartitions, string? chunkInterval = null)
        {
            var chunkIntervalSql = string.IsNullOrEmpty(chunkInterval) ? "" : $", chunk_time_interval => interval '{chunkInterval}'";

            var sql = $@"
                SELECT create_hypertable('{tableName}', '{timeColumnName}', '{spaceColumnName}', {numberPartitions}{chunkIntervalSql});
            ";

            ExecuteScript(connection, transaction, sql);
        }

        /// <summary>
        /// Adds a compression policy to a hypertable.
        /// </summary>
        protected virtual void AddCompressionPolicy(DbConnection connection, DbTransaction? transaction, string tableName, string compressAfterInterval)
        {
            var sql = $@"
                ALTER TABLE {tableName} SET (
                    timescaledb.compress,
                    timescaledb.compress_orderby = 'time',
                    timescaledb.compress_segmentby = 'device_id'
                );
                SELECT add_compression_policy('{tableName}', INTERVAL '{compressAfterInterval}');
            ";

            ExecuteScript(connection, transaction, sql);
        }

        /// <summary>
        /// Adds a retention policy to drop old data.
        /// </summary>
        protected virtual void AddRetentionPolicy(DbConnection connection, DbTransaction? transaction, string tableName, string dropAfterInterval)
        {
            var sql = $@"
                SELECT add_retention_policy('{tableName}', INTERVAL '{dropAfterInterval}');
            ";

            ExecuteScript(connection, transaction, sql);
        }

        /// <summary>
        /// Removes a compression policy from a hypertable.
        /// </summary>
        protected virtual void RemoveCompressionPolicy(DbConnection connection, DbTransaction? transaction, string tableName)
        {
            var sql = $@"
                SELECT remove_compression_policy('{tableName}');
            ";

            ExecuteScript(connection, transaction, sql);
        }

        /// <summary>
        /// Removes a retention policy from a hypertable.
        /// </summary>
        protected virtual void RemoveRetentionPolicy(DbConnection connection, DbTransaction? transaction, string tableName)
        {
            var sql = $@"
                SELECT remove_retention_policy('{tableName}');
            ";

            ExecuteScript(connection, transaction, sql);
        }

        /// <summary>
        /// Creates a continuous aggregate.
        /// </summary>
        protected virtual void CreateContinuousAggregate(DbConnection connection, DbTransaction? transaction, string viewName, string sourceTable, string timeBucket, string selectClause, string groupByClause = "")
        {
            var groupBySql = string.IsNullOrEmpty(groupByClause) ? "" : $", {groupByClause}";

            var sql = $@"
                CREATE MATERIALIZED VIEW {viewName}
                WITH (timescaledb.continuous) AS
                SELECT
                    time_bucket('{timeBucket}', time) AS bucket
                    {groupBySql},
                    {selectClause}
                FROM {sourceTable}
                GROUP BY bucket, {groupByClause};
            ";

            ExecuteScript(connection, transaction, sql);
        }

        /// <summary>
        /// Refreshes a continuous aggregate.
        /// </summary>
        protected virtual void RefreshContinuousAggregate(DbConnection connection, DbTransaction? transaction, string viewName)
        {
            var sql = $"CALL refresh_continuous_aggregate('{viewName}', NULL, NULL);";
            ExecuteScript(connection, transaction, sql);
        }

        /// <summary>
        /// Checks if a table is a hypertable.
        /// </summary>
        protected virtual bool IsHypertable(DbConnection connection, string tableName)
        {
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM timescaledb_information.hypertables WHERE hypertable_name = @table";
            AddParameter(command, "@table", tableName);

            var result = command.ExecuteScalar();
            return result != null && Convert.ToInt32(result) > 0;
        }

        /// <summary>
        /// Gets the chunk interval for a hypertable.
        /// </summary>
        protected virtual string? GetChunkInterval(DbConnection connection, string tableName)
        {
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT chunk_time_interval::text FROM timescaledb_information.hypertables WHERE hypertable_name = @table";
            AddParameter(command, "@table", tableName);

            var result = command.ExecuteScalar();
            return result?.ToString();
        }

        private void EnsureTimescaleDBExtension(DbConnection connection, DbTransaction? transaction)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "CREATE EXTENSION IF NOT EXISTS timescaledb;";
            try
            {
                command.ExecuteNonQuery();
            }
            catch
            {
                // Extension might already exist or different version
            }
        }
    }
}
