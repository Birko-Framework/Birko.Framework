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
    /// ambient boundary — a migration owns its transaction. PostgreSQL's DDL <i>is</i> transactional, so a
    /// migration that fails rolls its hypertable conversion back with it.
    /// </para>
    /// <para>
    /// <b>⚠ TASK-259 removed the reason this was the only option, so the choice is now open rather than
    /// forced.</b> TASK-253 rejected routing these through the connector because doing so required
    /// <c>SetExternalTransaction</c>, which published one caller's connection onto a connector cached
    /// process-wide per (type, settings id) — the mechanism both stores abandoned in TASK-240, and a live
    /// defect in its last caller. That mechanism is <b>gone</b>: <c>SqlSchemaBuilder</c> moved onto
    /// <see cref="Birko.Data.SQL.Connectors.AmbientSqlTransaction"/>, which is flow-scoped and restores on
    /// dispose, and the legacy pair was deleted outright. A migration can now publish its connection and
    /// transaction as an ambient boundary safely, so routing these emitters through the connector is a real
    /// option — it would let them reuse <c>DoDdlCommand</c> and the provider capabilities instead of
    /// hand-rolling their own execution. Left as-is here because it is a behaviour change on a live
    /// TimescaleDB path that wants its own measurement, not a comment edit: it is recorded so the next reader
    /// finds a decision that has been reopened rather than a constraint that no longer exists.
    /// </para>
    /// <para>
    /// <b>Schema-qualified names ARE supported</b> (TASK-262). Every object-name argument goes through
    /// <see cref="Birko.Data.SQL.Connectors.AbstractConnectorBase.QualifiedIdentifier"/>, which splits on
    /// <b>unquoted</b> dots and quotes each part — so <c>reporting.evts</c> emits
    /// <c>"reporting"."evts"</c> and reaches the real object. TASK-253 briefly broke this by quoting the whole
    /// name as one identifier, which asks for a single table whose name contains a period (measured on
    /// TimescaleDB 2.29.2 / PostgreSQL 16.15: <c>42P01</c>, and
    /// <c>PostgreSQLConnector.IsMissingTableException</c> classifies that as a missing table, so the handler
    /// could swallow it and report success). A table genuinely <i>named</i> <c>a.b</c> stays reachable as
    /// <c>"a.b"</c>, since only unquoted dots separate.
    /// </para>
    /// <para>
    /// <b>PRECONDITION, and this one is a real limit rather than a bug: these rules assume the object's
    /// COLUMNS were created by this framework's DDL.</b> The pre-fold in
    /// <see cref="Birko.Data.SQL.Connectors.AbstractConnectorBase.CatalogueNameLiteral"/> is correct because
    /// <c>AbstractConnector.CreateTable</c> and <c>SqlSchemaBuilder</c> provably emit column definitions
    /// <i>bare</i>, so PostgreSQL stores them folded. For an object whose columns a migration created with
    /// hand-written SQL that does not hold, and the consequence is measured and one-directional: a column
    /// created <b>quoted and mixed-case</b> — <c>CREATE TABLE metrics ("Timestamp" timestamptz)</c> — cannot
    /// be addressed through these emitters at all, because the fold turns <c>Timestamp</c> into
    /// <c>'timestamp'</c> and raises <c>42703</c>, and there is no spelling of the argument that reaches it.
    /// </para>
    /// <para>
    /// <b>Why that is documented rather than fixed, and what would have to change.</b> The fold cannot be
    /// made conditional on the caller's spelling, because these producers are shared with the <i>store</i>
    /// path (<c>TimescaleDBConnector.CreateHypertableSql</c>), where an unquoted name means "the quoted
    /// identifier this framework created" — the premise TASK-472 established. Teaching an unquoted name to
    /// mean "fold me" would re-break that defect, which was invisible precisely because the failure is
    /// swallowed. So supporting hand-created columns needs an explicit opt-out on these methods, and with
    /// <b>0</b> call sites of any emitter across all 16 consumer repos that is speculative API rather than a
    /// missing capability. Recorded as a limit so the next author meets a decision instead of a trap; if a
    /// real caller appears, the opt-out is the shape to add.
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
                ALTER TABLE {connector.QualifiedIdentifier(tableName)} SET (
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
                CREATE MATERIALIZED VIEW {connector.QualifiedIdentifier(viewName)}
                WITH (timescaledb.continuous) AS
                SELECT
                    time_bucket('{SqlLiteral.EscapeLiteral(timeBucket)}', time) AS bucket{groupBySql},
                    {selectClause}
                FROM {connector.QualifiedIdentifier(sourceTable)}
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
        /// Gets the chunk interval of a hypertable's <b>primary (time) dimension</b>, as text, or null when
        /// <paramref name="tableName"/> is not a hypertable.
        /// </summary>
        /// <remarks>
        /// <b>Targets TimescaleDB 2.x</b> (measured on 2.29.2 / PostgreSQL 16.15). It previously read
        /// <c>chunk_time_interval</c> from <c>timescaledb_information.hypertables</c>, which 2.0 removed: the
        /// interval moved to <c>timescaledb_information.dimensions</c> and was renamed <c>time_interval</c>.
        /// That query therefore raised <c>42703</c> on every 2.x server — i.e. every supported version — and it
        /// is not swallowed, so a migration calling it failed outright. The old spelling was presumably right
        /// on 1.x and nothing recorded that it had an expiry, which is why this remark now names the version
        /// the query targets (TASK-261).
        /// <para>
        /// <b><c>dimension_number = 1</c> is defensive, and the honest measurement says so.</b> The view holds
        /// one row per dimension: on a space-partitioned hypertable, dimension 1 is the time column carrying
        /// <c>time_interval</c> and dimension 2 is the space column with <b>both</b> interval columns NULL — so
        /// the unrestricted query returns 2 rows of which only 1 has a value, and <c>ExecuteScalar</c> takes
        /// the first. It currently takes the <i>right</i> one: <c>timescaledb_information.dimensions</c> carries
        /// its own <c>ORDER BY</c>, so removing this clause breaks nothing on 2.29.2 (measured — the revert
        /// fails no test). The clause stays because that correctness rests on an ordering the query does not
        /// state and the catalogue does not promise, and <b>this task exists precisely because a detail of this
        /// catalogue changed between versions</b>. Relying on the view's internal sort would be the same bet
        /// that produced the defect being fixed.
        /// </para>
        /// <para>
        /// <b>An integer-partitioned hypertable returns its integer interval, not null</b> — the coalesce is
        /// deliberate. Such a hypertable has a NULL <c>time_interval</c> and its width in
        /// <c>integer_interval</c> (measured: <c>100000</c>), so returning null would claim no interval is
        /// configured when one is. Note the discriminator is which column is populated, <b>not</b>
        /// <c>dimension_type</c>: an integer-partitioned dimension still reports <c>dimension_type = 'Time'</c>
        /// on 2.29.2, so branching on that would be wrong. The cost of coalescing is that a caller cannot tell
        /// <c>"3 days"</c> from <c>"100000"</c> without knowing the partitioning column's type — accepted,
        /// because the alternative is losing the value entirely, and a <c>string?</c> return can express
        /// neither shape better.
        /// </para>
        /// <para>Parameterised and case-preserving, for the reason given on <see cref="IsHypertable"/>.</para>
        /// </remarks>
        protected virtual string? GetChunkInterval(IMigrationContext context, string tableName)
        {
            var (connection, _, _) = GetSqlConnection(context);
            using var command = connection.CreateCommand();
            command.CommandText =
                "SELECT COALESCE(time_interval::text, integer_interval::text) "
              + "FROM timescaledb_information.dimensions "
              + "WHERE hypertable_name = @table AND dimension_number = 1";
            AddParameter(command, "@table", tableName);
            var result = command.ExecuteScalar();
            return result == DBNull.Value ? null : result?.ToString();
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
