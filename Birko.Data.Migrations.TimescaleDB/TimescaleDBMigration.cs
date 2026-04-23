using System;
using System.Data.Common;
using Birko.Data.Migrations.Context;
using Birko.Data.Migrations.SQL.Context;

namespace Birko.Data.Migrations.TimescaleDB
{
    /// <summary>
    /// Abstract base class for TimescaleDB migrations.
    /// Provides helper methods for creating hypertables, compression policies,
    /// retention policies, and continuous aggregates.
    /// </summary>
    public abstract class TimescaleDBMigration : Data.Migrations.IMigration
    {
        /// <inheritdoc/>
        public abstract long Version { get; }

        /// <inheritdoc/>
        public abstract string Name { get; }

        /// <inheritdoc/>
        public abstract string Description { get; }

        /// <inheritdoc/>
        public abstract DateTime CreatedAt { get; }

        /// <summary>
        /// Applies the migration. Override to provide TimescaleDB-specific Up logic.
        /// </summary>
        public abstract void Up(IMigrationContext context);

        /// <summary>
        /// Reverts the migration. Override to provide TimescaleDB-specific Down logic.
        /// </summary>
        public abstract void Down(IMigrationContext context);

        /// <summary>
        /// Creates a hypertable from a regular table.
        /// </summary>
        protected virtual void CreateHypertable(IMigrationContext context, string tableName, string timeColumnName, string? chunkInterval = null)
        {
            var (connection, transaction) = GetSqlConnection(context);
            var chunkIntervalSql = string.IsNullOrEmpty(chunkInterval) ? "" : $", chunk_time_interval => interval '{chunkInterval}'";
            var sql = $"SELECT create_hypertable('{tableName}', '{timeColumnName}'{chunkIntervalSql});";
            ExecuteScript(connection, transaction, sql);
        }

        /// <summary>
        /// Creates a hypertable with both time and space partitioning.
        /// </summary>
        protected virtual void CreateHypertableWithSpace(IMigrationContext context, string tableName, string timeColumnName, string spaceColumnName, int numberPartitions, string? chunkInterval = null)
        {
            var (connection, transaction) = GetSqlConnection(context);
            var chunkIntervalSql = string.IsNullOrEmpty(chunkInterval) ? "" : $", chunk_time_interval => interval '{chunkInterval}'";
            var sql = $"SELECT create_hypertable('{tableName}', '{timeColumnName}', '{spaceColumnName}', {numberPartitions}{chunkIntervalSql});";
            ExecuteScript(connection, transaction, sql);
        }

        /// <summary>
        /// Adds a compression policy to a hypertable.
        /// </summary>
        protected virtual void AddCompressionPolicy(IMigrationContext context, string tableName, string compressAfterInterval)
        {
            var (connection, transaction) = GetSqlConnection(context);
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
        protected virtual void AddRetentionPolicy(IMigrationContext context, string tableName, string dropAfterInterval)
        {
            var (connection, transaction) = GetSqlConnection(context);
            var sql = $"SELECT add_retention_policy('{tableName}', INTERVAL '{dropAfterInterval}');";
            ExecuteScript(connection, transaction, sql);
        }

        /// <summary>
        /// Removes a compression policy from a hypertable.
        /// </summary>
        protected virtual void RemoveCompressionPolicy(IMigrationContext context, string tableName)
        {
            var (connection, transaction) = GetSqlConnection(context);
            var sql = $"SELECT remove_compression_policy('{tableName}');";
            ExecuteScript(connection, transaction, sql);
        }

        /// <summary>
        /// Removes a retention policy from a hypertable.
        /// </summary>
        protected virtual void RemoveRetentionPolicy(IMigrationContext context, string tableName)
        {
            var (connection, transaction) = GetSqlConnection(context);
            var sql = $"SELECT remove_retention_policy('{tableName}');";
            ExecuteScript(connection, transaction, sql);
        }

        /// <summary>
        /// Creates a continuous aggregate.
        /// </summary>
        protected virtual void CreateContinuousAggregate(IMigrationContext context, string viewName, string sourceTable, string timeBucket, string selectClause, string groupByClause = "")
        {
            var (connection, transaction) = GetSqlConnection(context);
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
        protected virtual void RefreshContinuousAggregate(IMigrationContext context, string viewName)
        {
            var (connection, transaction) = GetSqlConnection(context);
            var sql = $"CALL refresh_continuous_aggregate('{viewName}', NULL, NULL);";
            ExecuteScript(connection, transaction, sql);
        }

        /// <summary>
        /// Checks if a table is a hypertable.
        /// </summary>
        protected virtual bool IsHypertable(IMigrationContext context, string tableName)
        {
            var (connection, _) = GetSqlConnection(context);
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM timescaledb_information.hypertables WHERE hypertable_name = @table";
            AddParameter(command, "@table", tableName);
            var result = command.ExecuteScalar();
            return result != null && Convert.ToInt32(result) > 0;
        }

        /// <summary>
        /// Gets the chunk interval for a hypertable.
        /// </summary>
        protected virtual string? GetChunkInterval(IMigrationContext context, string tableName)
        {
            var (connection, _) = GetSqlConnection(context);
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT chunk_time_interval::text FROM timescaledb_information.hypertables WHERE hypertable_name = @table";
            AddParameter(command, "@table", tableName);
            var result = command.ExecuteScalar();
            return result?.ToString();
        }

        private static (DbConnection connection, DbTransaction? transaction) GetSqlConnection(IMigrationContext context)
        {
            if (context is SqlMigrationContext sqlContext)
            {
                return (sqlContext.Connection, sqlContext.Transaction);
            }

            throw new InvalidOperationException($"Expected SqlMigrationContext but got {context.GetType().Name}.");
        }

        private static void ExecuteScript(DbConnection connection, DbTransaction? transaction, string sql)
        {
            if (string.IsNullOrWhiteSpace(sql))
                return;

            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = sql;
            command.ExecuteNonQuery();
        }

        private static void AddParameter(DbCommand command, string name, object value)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = name;
            parameter.Value = value;
            command.Parameters.Add(parameter);
        }
    }
}
