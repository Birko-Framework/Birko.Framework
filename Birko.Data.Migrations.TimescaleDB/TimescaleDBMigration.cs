using System;
using System.Data.Common;
using Birko.Data.Migrations.Context;
using Birko.Data.Migrations.SQL.Context;
using Birko.Data.SQL;
using Birko.Data.SQL.Connectors;

namespace Birko.Data.Migrations.TimescaleDB
{
    /// <summary>
    /// Abstract base class for TimescaleDB migrations.
    /// Provides helper methods for creating hypertables, compression policies,
    /// retention policies, and continuous aggregates.
    /// </summary>
    /// <remarks>
    /// <b>Every name a migration author passes in here is interpolated into the statement, because none of
    /// these arguments can be parameterised</b> — TimescaleDB's functions take a <c>regclass</c> or a
    /// <c>name</c> inside a quoted literal, and <c>ALTER TABLE</c> / <c>CREATE MATERIALIZED VIEW</c> take a
    /// real identifier. So each one is resolved through the connector's producers rather than escaped by
    /// hand; the rules live on <see cref="AbstractConnectorBase"/> (TASK-253) and there are four of them:
    /// <list type="bullet">
    /// <item><description>
    /// <b>A <c>regclass</c> inside a literal</b> — <c>create_hypertable</c>, the four policy functions,
    /// <c>refresh_continuous_aggregate</c> — goes through <see cref="AbstractConnectorBase.RegclassLiteral"/>:
    /// quoted as an identifier, then escaped for the literal. Emitted bare it folds, so a PascalCase table
    /// raises <c>42P01</c>. TASK-472 measured that one layer over, in
    /// <c>TimescaleDBConnector.BuildCreateHypertableSql</c>, where it was <i>swallowed</i> as a missing table
    /// and no hypertable was ever created for any PascalCase entity.
    /// </description></item>
    /// <item><description>
    /// <b>A <c>name</c> inside a literal</b> — the time and space columns of <c>create_hypertable</c> — goes
    /// through <see cref="AbstractConnectorBase.CatalogueNameLiteral"/>, which <i>pre-folds</i> and never
    /// quotes: it is compared textually against <c>pg_attribute.attname</c>, and this framework emits column
    /// definitions bare (TASK-209).
    /// </description></item>
    /// <item><description>
    /// <b>A real identifier</b> — <c>ALTER TABLE {table}</c>, <c>CREATE MATERIALIZED VIEW {view}</c>,
    /// <c>FROM {table}</c> — goes through <see cref="AbstractConnectorBase.QuoteIdentifier"/> only, with no
    /// literal escaping and no folding. § Conventions: quote table identifiers, never quote column
    /// identifiers.
    /// </description></item>
    /// <item><description>
    /// <b>An expression fragment</b> — <c>compress_orderby</c>, <c>compress_segmentby</c>, the time bucket,
    /// every INTERVAL — is <i>not</i> an identifier and gets <see cref="SqlLiteral.EscapeLiteral"/> only. It
    /// must not be folded (the parser parses it, so its own folding applies) and must not be
    /// identifier-validated, because <c>ts DESC</c> and <c>date_trunc('day', x)</c> are legitimate values.
    /// Sitting inside a literal, escaping contains it completely.
    /// </description></item>
    /// </list>
    /// <para>
    /// <b>These statements run on the migration's own connection and transaction, deliberately.</b> They do
    /// not go through <c>AbstractConnector.DoDdlCommand</c>, so they neither join nor are suppressed off an
    /// ambient boundary — a migration owns its transaction, which is the same reason TASK-243 leaves the
    /// legacy <c>ExternalConnection</c> pair unsuppressed. Routing them through the connector would require
    /// <c>SetExternalTransaction</c>, which publishes one caller's connection onto a connector cached
    /// process-wide per (type, settings id) — the mechanism both stores deliberately abandoned in TASK-240,
    /// and a live defect in its last caller (TASK-259). PostgreSQL's DDL <i>is</i> transactional, so a
    /// migration that fails rolls its hypertable conversion back with it.
    /// </para>
    /// <para>
    /// <b>Two arguments are raw SQL and cannot be contained</b> — see
    /// <see cref="BuildContinuousAggregateSql"/>'s <c>selectClause</c> and <c>groupByClause</c>. TASK-260
    /// owns replacing them with a structured surface.
    /// </para>
    /// </remarks>
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
            var (connection, transaction, connector) = GetSqlConnection(context);
            ExecuteScript(connection, transaction, BuildCreateHypertableSql(connector, tableName, timeColumnName, chunkInterval));
        }

        /// <summary>
        /// Builds the <c>create_hypertable</c> DDL. The table is a <c>regclass</c> and carries its own
        /// identifier quotes; the time column is a <c>name</c> and is pre-folded instead. Opposite
        /// treatments in one statement — see the class remarks.
        /// </summary>
        internal static string BuildCreateHypertableSql(AbstractConnector connector, string tableName, string timeColumnName, string? chunkInterval = null)
        {
            var chunkIntervalSql = string.IsNullOrEmpty(chunkInterval)
                ? ""
                : $", chunk_time_interval => interval '{SqlLiteral.EscapeLiteral(chunkInterval)}'";
            return $"SELECT create_hypertable('{connector.RegclassLiteral(tableName)}', '{connector.CatalogueNameLiteral(timeColumnName)}'{chunkIntervalSql});";
        }

        /// <summary>
        /// Creates a hypertable with both time and space partitioning.
        /// </summary>
        protected virtual void CreateHypertableWithSpace(IMigrationContext context, string tableName, string timeColumnName, string spaceColumnName, int numberPartitions, string? chunkInterval = null)
        {
            var (connection, transaction, connector) = GetSqlConnection(context);
            ExecuteScript(connection, transaction, BuildCreateHypertableWithSpaceSql(connector, tableName, timeColumnName, spaceColumnName, numberPartitions, chunkInterval));
        }

        /// <summary>
        /// Builds the space-partitioned <c>create_hypertable</c> DDL. Both column arguments are <c>name</c>s
        /// and are pre-folded; <paramref name="numberPartitions"/> is an <see cref="int"/> and needs no
        /// escaping.
        /// </summary>
        internal static string BuildCreateHypertableWithSpaceSql(AbstractConnector connector, string tableName, string timeColumnName, string spaceColumnName, int numberPartitions, string? chunkInterval = null)
        {
            var chunkIntervalSql = string.IsNullOrEmpty(chunkInterval)
                ? ""
                : $", chunk_time_interval => interval '{SqlLiteral.EscapeLiteral(chunkInterval)}'";
            return $"SELECT create_hypertable('{connector.RegclassLiteral(tableName)}', '{connector.CatalogueNameLiteral(timeColumnName)}', '{connector.CatalogueNameLiteral(spaceColumnName)}', {numberPartitions}{chunkIntervalSql});";
        }

        /// <summary>
        /// Adds a compression policy to a hypertable.
        /// </summary>
        protected virtual void AddCompressionPolicy(IMigrationContext context, string tableName, string compressAfterInterval, string orderByColumn = "time", string? segmentByColumn = null)
        {
            var (connection, transaction, connector) = GetSqlConnection(context);
            ExecuteScript(connection, transaction, BuildCompressionPolicySql(connector, tableName, compressAfterInterval, orderByColumn, segmentByColumn));
        }

        /// <summary>
        /// Builds the compression-policy DDL. Don't hardcode the order/segment columns
        /// (CR-H070: 'time'/'device_id' fail on any table without a literal device_id column and are
        /// wrong for most schemas). orderby defaults to the conventional 'time'; segmentby is opt-in
        /// and omitted when not supplied.
        /// </summary>
        /// <remarks>
        /// <b>The table appears twice, needing two different treatments</b>, which is why this method is the
        /// clearest example of the class remarks: <c>ALTER TABLE</c> takes a real identifier and gets
        /// <see cref="AbstractConnectorBase.QuoteIdentifier"/>, while <c>add_compression_policy</c> takes a
        /// <c>regclass</c> inside a literal and gets <see cref="AbstractConnectorBase.RegclassLiteral"/>.
        /// Reasoning from either one alone produces a statement that is broken at the other.
        /// <para>
        /// The <c>compress_orderby</c> / <c>compress_segmentby</c> values are expression fragments, not
        /// identifiers — <c>ts DESC</c> is legitimate — so they are escaped for their literal and nothing
        /// more. Escaping is complete containment there, since they sit inside quotes.
        /// </para>
        /// </remarks>
        internal static string BuildCompressionPolicySql(AbstractConnector connector, string tableName, string compressAfterInterval, string orderByColumn = "time", string? segmentByColumn = null)
        {
            var segmentBySql = string.IsNullOrEmpty(segmentByColumn)
                ? ""
                : $",\n                    timescaledb.compress_segmentby = '{SqlLiteral.EscapeLiteral(segmentByColumn)}'";
            return $@"
                ALTER TABLE {connector.QuoteIdentifier(tableName)} SET (
                    timescaledb.compress,
                    timescaledb.compress_orderby = '{SqlLiteral.EscapeLiteral(orderByColumn)}'{segmentBySql}
                );
                SELECT add_compression_policy('{connector.RegclassLiteral(tableName)}', INTERVAL '{SqlLiteral.EscapeLiteral(compressAfterInterval)}');
            ";
        }

        /// <summary>
        /// Adds a retention policy to drop old data.
        /// </summary>
        protected virtual void AddRetentionPolicy(IMigrationContext context, string tableName, string dropAfterInterval)
        {
            var (connection, transaction, connector) = GetSqlConnection(context);
            ExecuteScript(connection, transaction, BuildRetentionPolicySql(connector, tableName, dropAfterInterval));
        }

        /// <summary>Builds the retention-policy DDL. The table is a <c>regclass</c>; the interval is a fragment.</summary>
        internal static string BuildRetentionPolicySql(AbstractConnector connector, string tableName, string dropAfterInterval)
            => $"SELECT add_retention_policy('{connector.RegclassLiteral(tableName)}', INTERVAL '{SqlLiteral.EscapeLiteral(dropAfterInterval)}');";

        /// <summary>
        /// Removes a compression policy from a hypertable.
        /// </summary>
        protected virtual void RemoveCompressionPolicy(IMigrationContext context, string tableName)
        {
            var (connection, transaction, connector) = GetSqlConnection(context);
            ExecuteScript(connection, transaction, BuildRemoveCompressionPolicySql(connector, tableName));
        }

        /// <summary>Builds the compression-policy removal. The table is a <c>regclass</c>.</summary>
        internal static string BuildRemoveCompressionPolicySql(AbstractConnector connector, string tableName)
            => $"SELECT remove_compression_policy('{connector.RegclassLiteral(tableName)}');";

        /// <summary>
        /// Removes a retention policy from a hypertable.
        /// </summary>
        protected virtual void RemoveRetentionPolicy(IMigrationContext context, string tableName)
        {
            var (connection, transaction, connector) = GetSqlConnection(context);
            ExecuteScript(connection, transaction, BuildRemoveRetentionPolicySql(connector, tableName));
        }

        /// <summary>Builds the retention-policy removal. The table is a <c>regclass</c>.</summary>
        internal static string BuildRemoveRetentionPolicySql(AbstractConnector connector, string tableName)
            => $"SELECT remove_retention_policy('{connector.RegclassLiteral(tableName)}');";

        /// <summary>
        /// Creates a continuous aggregate.
        /// </summary>
        protected virtual void CreateContinuousAggregate(IMigrationContext context, string viewName, string sourceTable, string timeBucket, string selectClause, string groupByClause = "")
        {
            var (connection, transaction, connector) = GetSqlConnection(context);
            ExecuteScript(connection, transaction, BuildContinuousAggregateSql(connector, viewName, sourceTable, timeBucket, selectClause, groupByClause));
        }

        /// <summary>
        /// Builds the continuous-aggregate DDL. Reuses the guarded groupBySql for the GROUP BY too:
        /// an empty groupByClause previously emitted "GROUP BY bucket, " with a dangling comma
        /// (invalid SQL) (CR-H071).
        /// </summary>
        /// <remarks>
        /// <b><paramref name="selectClause"/> and <paramref name="groupByClause"/> are interpolated as raw
        /// SQL and CANNOT be contained.</b> They are not identifiers and not literals — they are expression
        /// lists, so a caller controls the statement through them. Do not build either from untrusted input.
        /// <para>
        /// They are deliberately <i>not</i> identifier-validated, and a test pins that: a group-by may
        /// legitimately be an expression such as <c>date_trunc('day', x)</c>, and a select clause is a list of
        /// aggregates by definition, so validating them would refuse working migrations while leaving the
        /// other argument open anyway. The containment can only come from changing the API's shape, which
        /// TASK-260 owns.
        /// </para>
        /// <para>
        /// <b>The bucketing column is still the hardcoded literal <c>time</c></b> — CR-H070's defect, left
        /// unfixed in this method when its sibling above was corrected. No framework-created table has such a
        /// column, since column definitions are emitted bare and every Birko entity is PascalCase. TASK-255
        /// owns it; this method's identifier handling is fixed here regardless.
        /// </para>
        /// </remarks>
        internal static string BuildContinuousAggregateSql(AbstractConnector connector, string viewName, string sourceTable, string timeBucket, string selectClause, string groupByClause = "")
        {
            var groupBySql = string.IsNullOrEmpty(groupByClause) ? "" : $", {groupByClause}";
            return $@"
                CREATE MATERIALIZED VIEW {connector.QuoteIdentifier(viewName)}
                WITH (timescaledb.continuous) AS
                SELECT
                    time_bucket('{SqlLiteral.EscapeLiteral(timeBucket)}', time) AS bucket{groupBySql},
                    {selectClause}
                FROM {connector.QuoteIdentifier(sourceTable)}
                GROUP BY bucket{groupBySql};
            ";
        }

        /// <summary>
        /// Refreshes a continuous aggregate.
        /// </summary>
        protected virtual void RefreshContinuousAggregate(IMigrationContext context, string viewName)
        {
            var (connection, transaction, connector) = GetSqlConnection(context);
            ExecuteScript(connection, transaction, BuildRefreshContinuousAggregateSql(connector, viewName));
        }

        /// <summary>
        /// Builds the continuous-aggregate refresh. The view is a <c>regclass</c> here — note the contrast with
        /// <see cref="BuildContinuousAggregateSql"/>, where the same view name sits in a real identifier
        /// position and is quoted without literal escaping.
        /// </summary>
        internal static string BuildRefreshContinuousAggregateSql(AbstractConnector connector, string viewName)
            => $"CALL refresh_continuous_aggregate('{connector.RegclassLiteral(viewName)}', NULL, NULL);";

        /// <summary>
        /// Checks if a table is a hypertable.
        /// </summary>
        /// <remarks>
        /// <b>Parameterised, and the name is passed through unfolded — deliberately.</b>
        /// <c>timescaledb_information.hypertables.hypertable_name</c> stores the name with its case intact, so
        /// a PascalCase table is found by its PascalCase name. Folding it here (for symmetry with
        /// <see cref="BuildCreateHypertableSql"/>, which must fold its <i>column</i>) would break this: a
        /// lowercased lookup returns 0 for a hypertable that exists, which is how TASK-472 briefly mistook a
        /// working fix for a broken one.
        /// </remarks>
        protected virtual bool IsHypertable(IMigrationContext context, string tableName)
        {
            var (connection, _, _) = GetSqlConnection(context);
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM timescaledb_information.hypertables WHERE hypertable_name = @table";
            AddParameter(command, "@table", tableName);
            var result = command.ExecuteScalar();
            return result != null && Convert.ToInt32(result) > 0;
        }

        /// <summary>
        /// Gets the chunk interval for a hypertable.
        /// </summary>
        /// <remarks>Parameterised and case-preserving, for the reason given on <see cref="IsHypertable"/>.</remarks>
        protected virtual string? GetChunkInterval(IMigrationContext context, string tableName)
        {
            var (connection, _, _) = GetSqlConnection(context);
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT chunk_time_interval::text FROM timescaledb_information.hypertables WHERE hypertable_name = @table";
            AddParameter(command, "@table", tableName);
            var result = command.ExecuteScalar();
            return result?.ToString();
        }

        private static (DbConnection connection, DbTransaction? transaction, AbstractConnector connector) GetSqlConnection(IMigrationContext context)
        {
            if (context is SqlMigrationContext sqlContext)
            {
                return (sqlContext.Connection, sqlContext.Transaction, sqlContext.Connector);
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
