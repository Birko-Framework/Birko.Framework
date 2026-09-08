using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
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
    /// <c>FROM {table}</c> — goes through <see cref="AbstractConnectorBase.QualifiedIdentifier"/> only, with
    /// no literal escaping and no folding. § Conventions: quote table identifiers, never quote column
    /// identifiers. <b>Not <see cref="AbstractConnectorBase.QuoteIdentifier"/></b>, which quotes its whole
    /// argument as ONE identifier — this text said so until TASK-281's close gate, and an author following it
    /// would reintroduce TASK-262's regression, where <c>reporting.evts</c> became a request for a single
    /// table whose name contains a period (<c>42P01</c>, which
    /// <c>PostgreSQLConnector.IsMissingTableException</c> can swallow).
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
    /// migration that fails rolls its hypertable conversion back with it. <c>create_hypertable</c> and the
    /// policy functions are all transaction-safe, measured.
    /// </para>
    /// <para>
    /// <b>⚠ TWO statements are the exception, and the sentence above is not a blanket guarantee</b>
    /// (TASK-281, measured on TimescaleDB 2.29.2 / PostgreSQL 16.15). Both raise SQLSTATE <c>25001</c> inside
    /// a transaction block:
    /// <list type="bullet">
    /// <item><description>
    /// <c>CREATE MATERIALIZED VIEW … WITH (timescaledb.continuous)</c> — because without <c>WITH NO DATA</c>
    /// it performs an initial refresh. <see cref="BuildContinuousAggregateSql"/> therefore always emits
    /// <c>WITH NO DATA</c>, and the view is empty until populated.
    /// </description></item>
    /// <item><description>
    /// <c>refresh_continuous_aggregate()</c> — which has no such escape and cannot be made transactional at
    /// all, so <see cref="RefreshContinuousAggregate"/> refuses inside a transaction and names the two ways
    /// out.
    /// </description></item>
    /// </list>
    /// The transaction-safe way to keep an aggregate current is therefore
    /// <see cref="AddContinuousAggregatePolicy"/>, whose <c>add_continuous_aggregate_policy</c> IS legal in a
    /// transaction and whose job survives the commit — but note it refreshes a <b>moving window</b>, so a
    /// non-null <c>startOffset</c> never materialises older history. Backfilling existing history still
    /// requires a refresh off a transaction. See that method's remarks.
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
    /// <b>Schema-qualified names ARE supported by the EMITTERS</b> (TASK-262) — but <b>not by the two
    /// catalogue READERS</b>, and this paragraph claimed otherwise until TASK-260's close gate.
    /// <see cref="IsHypertable"/> and <see cref="GetChunkInterval"/> compare the caller's name against
    /// <c>timescaledb_information.hypertables.hypertable_name</c>, which holds the <i>bare</i> name with the
    /// schema in a separate column — so <c>IsHypertable(context, "reporting.evts")</c> answers <b>false</b>
    /// for a hypertable that exists, and the common guard
    /// <c>if (!IsHypertable(t)) CreateHypertable(t)</c> then re-issues the conversion. <b>[[TASK-280]] owns
    /// that</b>; it is stated here because a remark asserting the opposite is worse than no remark.
    /// <para>
    /// Every object-name argument to an <i>emitter</i> goes through
    /// <see cref="Birko.Data.SQL.Connectors.AbstractConnectorBase.QualifiedIdentifier"/>, which splits on
    /// <b>unquoted</b> dots and quotes each part — so <c>reporting.evts</c> emits
    /// <c>"reporting"."evts"</c> and reaches the real object. TASK-253 briefly broke this by quoting the whole
    /// name as one identifier, which asks for a single table whose name contains a period (measured on
    /// TimescaleDB 2.29.2 / PostgreSQL 16.15: <c>42P01</c>, and
    /// <c>PostgreSQLConnector.IsMissingTableException</c> classifies that as a missing table, so the handler
    /// could swallow it and report success). A table genuinely <i>named</i> <c>a.b</c> stays reachable as
    /// <c>"a.b"</c>, since only unquoted dots separate.
    /// </para>
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
    /// <b>Every caller-derived input in this class is now contained — the last two that were not are gone</b>
    /// (TASK-260). <see cref="BuildContinuousAggregateSql"/> used to take <c>selectClause</c> and
    /// <c>groupByClause</c> as raw SQL in statement position, which no escaping can contain: a string
    /// documented as "SQL" has no containment story, and the only fix is to stop taking SQL. They are
    /// replaced by <see cref="ContinuousAggregateProjection"/> and
    /// <see cref="ContinuousAggregateGrouping"/>, whose identifiers are validated and whose one literal is
    /// escaped.
    /// <para>
    /// <b>The aggregate function is a validated identifier, deliberately NOT a closed enum</b> — measured on
    /// TimescaleDB 2.29.2, a continuous aggregate accepts essentially any aggregate, including
    /// <c>array_agg</c>, <c>string_agg</c>, <c>bool_and</c>, the ordered-set <c>percentile_cont</c> and
    /// user-defined ones, so an enum would refuse aggregates that work today. This is not a passthrough:
    /// arbitrary text fails the guard, and a name that merely does not exist can only fail the statement at
    /// DDL time (<c>42883</c>, naming the function and its argument types). Ordered-set aggregates remain
    /// inexpressible — their syntax is not function-plus-arguments — and want their own structured shape if
    /// a caller ever needs one, never a raw string.
    /// </para>
    /// </para>
    /// <para>
    /// <b>A fourth position: a bare column reference, contained by REFUSAL rather than by escaping</b>
    /// (TASK-255). <see cref="BuildContinuousAggregateSql"/>'s <c>timeColumn</c> sits in real identifier
    /// position inside the view body, so it must be emitted bare to resolve the folded column that
    /// bare-column <c>CREATE TABLE</c> creates — which means no quote character encloses it and escaping
    /// would contain nothing. It is guarded by
    /// <see cref="Birko.Data.SQL.DataBase.ValidateColumnIdentifier"/> instead. So this class now has three
    /// containment mechanisms, not two: literal escaping, identifier quoting, and refusal.
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
        /// <para>
        /// <b>CR-H070 is now closed in both methods</b> — <see cref="BuildContinuousAggregateSql"/> kept the
        /// hardcoded bucketing column until TASK-255, with this very comment sitting four lines above it.
        /// <b>One half of the finding remains here:</b> the <c>orderByColumn = "time"</c> default was added
        /// by commit <c>531d816</c> to keep then-existing callers compiling, and no framework-created table
        /// can have a column of that name — so it is a default that cannot work, which TASK-279 owns.
        /// Its value is an expression fragment (<c>ts DESC</c> is legitimate), so it is escaped for its
        /// literal and deliberately <i>not</i> identifier-validated: do not "unify" it with
        /// <see cref="BuildContinuousAggregateSql"/>'s column guard.
        /// </para>
        /// </summary>
        /// <remarks>
        /// <b>The table appears twice, needing two different treatments</b>, which is why this method is the
        /// clearest example of the class remarks: <c>ALTER TABLE</c> takes a real identifier and gets
        /// <see cref="AbstractConnectorBase.QualifiedIdentifier"/>, while <c>add_compression_policy</c> takes a
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
        protected virtual void CreateContinuousAggregate(IMigrationContext context, string viewName,
            string sourceTable, string timeBucket, string timeColumn,
            IEnumerable<ContinuousAggregateProjection> projections,
            IEnumerable<ContinuousAggregateGrouping>? groupings = null)
        {
            var (connection, transaction, connector) = GetSqlConnection(context);
            ExecuteScript(connection, transaction,
                BuildContinuousAggregateSql(connector, viewName, sourceTable, timeBucket, timeColumn, projections, groupings));
        }

        /// <summary>
        /// Builds the continuous-aggregate DDL. Reuses the guarded groupBySql for the GROUP BY too:
        /// an empty groupByClause previously emitted "GROUP BY bucket, " with a dangling comma
        /// (invalid SQL) (CR-H071).
        /// <para>
        /// <b>Always emits <c>WITH NO DATA</c>, so the view is EMPTY until something populates it</b>
        /// (TASK-281). Without it the statement performs an initial refresh, which raises SQLSTATE
        /// <c>25001</c> — <i>"CREATE MATERIALIZED VIEW ... WITH DATA cannot run inside a transaction
        /// block"</i> — and <c>SqlMigrationSettings.UseTransaction</c> defaults to <see langword="true"/>,
        /// so through the runner this could only ever fail. Measured on TimescaleDB 2.29.2 / PostgreSQL
        /// 16.15.
        /// </para>
        /// <para>
        /// <b>Unconditional rather than "only when a transaction is present", deliberately.</b> The
        /// conditional version makes the identical migration yield a populated or an empty view depending on
        /// a settings flag, with nothing at the call site saying which — two doors onto one feature giving
        /// different answers (§ Conventions, TASK-274). Uniform emptiness is a rule a caller can hold in
        /// their head. Keep it current with <see cref="AddContinuousAggregatePolicy"/> (transaction-safe,
        /// but a moving window — see its remarks) or backfill history with
        /// <see cref="RefreshContinuousAggregate"/>, off a transaction.
        /// </para>
        /// </summary>
        /// <remarks>
        /// <b><paramref name="projections"/> and <paramref name="groupings"/> are structured values, not SQL</b>
        /// (TASK-260). They replaced a <c>selectClause</c> / <c>groupByClause</c> pair that was raw SQL in
        /// statement position and therefore uncontainable by any escaping — a string documented as "SQL" has
        /// no containment story, so the fix was the API's shape rather than a validator. Every identifier
        /// they carry is refused unless it is a bare identifier; the one literal a grouping may carry is
        /// escaped.
        /// </para>
        /// <para>
        /// <b><paramref name="timeColumn"/> is a fourth treatment again, and it is emitted BARE</b> (CR-H070,
        /// TASK-255). It is a column reference in real identifier position inside the view body, so neither
        /// of the literal treatments applies: <see cref="AbstractConnectorBase.CatalogueNameLiteral"/> is for
        /// a <c>name</c> compared textually against a catalogue column, and escaping alone contains nothing
        /// outside quotes. Bare is also what resolves — <c>CreateTable</c> emits column definitions bare, so
        /// PostgreSQL stores them folded, and a quoted <c>"Ts"</c> would not match the stored <c>ts</c>.
        /// </para>
        /// <para>
        /// Bare removes the accidental containment quoting was providing, so the argument is guarded by
        /// <see cref="Birko.Data.SQL.DataBase.ValidateColumnIdentifier"/> — the sanctioned weaker tier, since
        /// this class holds a table name and no entity type. It refuses every measured payload but cannot fix
        /// a <c>[NamedField]</c> remapping, and it rejects a <c>Table.</c> qualifier (this statement
        /// introduces no alias).
        /// </para>
        /// <para>
        /// <b>Limit, recorded rather than fixed:</b> a hand-created <i>quoted mixed-case</i> column is
        /// unreachable through this emitter, because bare folds. Same family as
        /// <see cref="AbstractConnectorBase.CatalogueNameLiteral"/>'s documented limit; with no caller needing
        /// it, an opt-out would be speculative API.
        /// </para>
        /// <para>
        /// <b>It is required, with no default</b>, unlike <see cref="BuildCompressionPolicySql"/>'s
        /// <c>orderByColumn</c>. That default was a source-compatibility artefact of commit
        /// <c>531d816</c> — the parameter did not exist before it — not a judgement that <c>"time"</c> is a
        /// good value; no framework-created table can have such a column. This method's convention instead
        /// follows <see cref="BuildCreateHypertableSql"/>, where a time-dimension column is required.
        /// </para>
        /// </remarks>
        internal static string BuildContinuousAggregateSql(AbstractConnector connector, string viewName,
            string sourceTable, string timeBucket, string timeColumn,
            IEnumerable<ContinuousAggregateProjection> projections,
            IEnumerable<ContinuousAggregateGrouping>? groupings = null)
        {
            if (projections == null)
            {
                throw new ArgumentNullException(nameof(projections));
            }

            var projectionSql = string.Join(", ", projections.Select(p => p.Render()));
            if (string.IsNullOrEmpty(projectionSql))
            {
                throw new ArgumentException(
                    "A continuous aggregate must project at least one aggregate; an aggregate view over no "
                    + "aggregates is not a view anyone wants and PostgreSQL would reject the statement.",
                    nameof(projections));
            }

            // CR-H071 is about the DANGLING COMMA, and that is decided once, here, from whether the grouping
            // set is empty -- not from the two clauses rendering identically. They deliberately differ: the
            // SELECT list carries `AS alias` and the GROUP BY must not (PostgreSQL groups by the expression;
            // an alias there is a syntax error). Without that split, two expression groupings using the same
            // function both take the function's name as their output column and the statement fails with
            // 42701 "column ... specified more than once" -- measured on 2.29.2, and found by code-review at
            // TASK-260's close gate, where the redesign had claimed to preserve the expression-grouping
            // capability while quietly dropping the ability to alias it.
            var materialised = groupings?.ToArray() ?? System.Array.Empty<ContinuousAggregateGrouping>();
            var selectGroupingSql = string.Join(", ", materialised.Select(g => g.RenderSelect()));
            var byGroupingSql = string.Join(", ", materialised.Select(g => g.Render()));
            var selectBySql = materialised.Length == 0 ? string.Empty : $", {selectGroupingSql}";
            var groupBySql = materialised.Length == 0 ? string.Empty : $", {byGroupingSql}";

            return $@"
                CREATE MATERIALIZED VIEW {connector.QualifiedIdentifier(viewName)}
                WITH (timescaledb.continuous) AS
                SELECT
                    time_bucket('{SqlLiteral.EscapeLiteral(timeBucket)}', {Birko.Data.SQL.DataBase.ValidateColumnIdentifier(timeColumn, nameof(timeColumn))}) AS bucket{selectBySql},
                    {projectionSql}
                FROM {connector.QualifiedIdentifier(sourceTable)}
                GROUP BY bucket{groupBySql}
                WITH NO DATA;
            ";
        }

        /// <summary>
        /// Refreshes a continuous aggregate. <b>Cannot run inside a transaction</b> — see the remarks.
        /// </summary>
        /// <remarks>
        /// <b><c>refresh_continuous_aggregate()</c> raises SQLSTATE <c>25001</c> inside a transaction block</b>
        /// (measured on TimescaleDB 2.29.2 / PostgreSQL 16.15), and
        /// <c>SqlMigrationSettings.UseTransaction</c> defaults to <see langword="true"/> — so through the
        /// runner's default configuration this could only ever fail (TASK-281).
        /// <para>
        /// It therefore refuses up front. <b>The server's own message is perfectly clear and that is NOT the
        /// reason this guard exists</b> — what the server cannot know is <c>UseTransaction</c>, or that this
        /// framework has a policy emitter. Routing is the only thing the framework adds here, which is why
        /// the message names <i>both</i> doors rather than merely saying no (§ SH-H037 / TASK-215: a refusal
        /// names the door THIS caller has).
        /// </para>
        /// <para>
        /// <b>The version stamp is load-bearing.</b> If TimescaleDB ever relaxes the restriction this guard
        /// becomes a <i>false refusal</i>, which this codebase rates worse than the hole
        /// (<c>PredicateScope</c>: a false refusal breaks working code). The measured version is recorded so
        /// that becomes findable rather than mysterious — the catalogue-drift rule from TASK-261.
        /// </para>
        /// </remarks>
        /// <exception cref="InvalidOperationException">The migration is running inside a transaction.</exception>
        protected virtual void RefreshContinuousAggregate(IMigrationContext context, string viewName)
        {
            var (connection, transaction, connector) = GetSqlConnection(context);
            if (transaction != null)
            {
                throw new InvalidOperationException(
                    $"refresh_continuous_aggregate() cannot run inside a transaction block (SQLSTATE 25001), "
                    + $"and this migration is running in one. Two ways out for '{viewName}', and they are "
                    + "NOT equivalent: AddContinuousAggregatePolicy(...) is transaction-safe and keeps the "
                    + "aggregate CURRENT, but a non-null startOffset means it only ever refreshes the window "
                    + "[now() - startOffset, now() - endOffset], so older history is never materialised "
                    + "(pass a null startOffset to include it). To BACKFILL existing history immediately, "
                    + "run this migration with SqlMigrationSettings.UseTransaction = false.");
            }
            ExecuteScript(connection, transaction, BuildRefreshContinuousAggregateSql(connector, viewName));
        }

        /// <summary>
        /// Adds a refresh policy to a continuous aggregate — the transaction-safe way to keep one
        /// <b>current</b>. Read the remarks before assuming it also backfills history: usually it does not.
        /// </summary>
        /// <remarks>
        /// <b>Measured legal inside a transaction block, with the job surviving the commit</b> (TimescaleDB
        /// 2.29.2 / PostgreSQL 16.15). That is what makes a transactional migration merely <i>limited</i>
        /// rather than unable to populate an aggregate at all: <c>CREATE … WITH NO DATA</c> plus a policy is
        /// the workflow, and the background job does the filling (TASK-281).
        /// <para>
        /// <b>⚠ The policy refreshes a MOVING WINDOW, so a non-null <paramref name="startOffset"/> never
        /// materialises older history.</b> The job covers <c>[now() - startOffset, now() - endOffset]</c> and
        /// nothing before it. Measured: a hypertable holding one row <b>400 days</b> old and one row 2 days
        /// old, with <c>startOffset = "30 days"</c>, yields exactly <b>one</b> bucket once the job runs — the
        /// recent one. The old bucket is absent permanently, with no error anywhere. Pass a
        /// <see langword="null"/> <paramref name="startOffset"/> to cover all history, or backfill with
        /// <see cref="RefreshContinuousAggregate"/> off a transaction.
        /// </para>
        /// <para>
        /// Documented rather than guarded because the caller has no other signal — an under-filled aggregate
        /// reads exactly like a correctly-filled one. Found at TASK-281's close gate, where the first version
        /// of this API called a policy "the transaction-safe way to populate an aggregate" full stop, which
        /// is true only for a null <paramref name="startOffset"/>.
        /// </para>
        /// <para>
        /// The view is a <c>regclass</c> inside a literal, so it takes
        /// <see cref="AbstractConnectorBase.RegclassLiteral"/>. The three offsets are <b>expression
        /// fragments</b> inside literals — an INTERVAL is a legitimate expression exactly as
        /// <c>compress_orderby</c>'s <c>ts DESC</c> is — so they get
        /// <see cref="SqlLiteral.EscapeLiteral"/> and are deliberately not identifier-validated.
        /// </para>
        /// <para>
        /// <paramref name="startOffset"/> is nullable because TimescaleDB accepts <c>NULL</c> there to mean
        /// "from the beginning of time"; <c>NULL</c> is emitted unquoted, since a quoted <c>'NULL'</c> would
        /// be the string rather than the keyword. Measured on 2.29.2: a <b>bare untyped</b> <c>NULL</c> is
        /// accepted by <c>add_continuous_aggregate_policy</c> despite its parameters being declared
        /// <c>"any"</c>, and the job's config records <c>"start_offset": null</c> — so the door
        /// <see cref="RefreshContinuousAggregate"/>'s refusal points at really does open, and there is a
        /// live test saying so rather than a rendering assertion (TASK-284).
        /// </para>
        /// <para>
        /// ⚠ <b><see langword="null"/> and <c>""</c> are different, and only <see langword="null"/> means
        /// all of history.</b> An empty <paramref name="startOffset"/> is an <b>error</b>, not a synonym —
        /// it reaches <c>INTERVAL ''</c> and PostgreSQL rejects it with
        /// <c>invalid input syntax for type interval: ""</c>. Until TASK-284 this method used
        /// <c>IsNullOrEmpty</c>, so a configuration value that came back empty rather than null silently
        /// produced a far heavier policy than the author intended — every chunk, on every run. It is
        /// spelled out here because the caller has no other signal that the two differ, and because the
        /// wrong one fails by working.
        /// </para>
        /// </remarks>
        protected virtual void AddContinuousAggregatePolicy(IMigrationContext context, string viewName,
            string? startOffset, string endOffset, string scheduleInterval)
        {
            var (connection, transaction, connector) = GetSqlConnection(context);
            ExecuteScript(connection, transaction,
                BuildContinuousAggregatePolicySql(connector, viewName, startOffset, endOffset, scheduleInterval));
        }

        /// <summary>
        /// Builds the continuous-aggregate refresh-policy DDL. The view is a <c>regclass</c>; the three
        /// offsets are expression fragments. See <see cref="AddContinuousAggregatePolicy"/>.
        /// </summary>
        internal static string BuildContinuousAggregatePolicySql(AbstractConnector connector, string viewName,
            string? startOffset, string endOffset, string scheduleInterval)
        {
            // TASK-284 -- `== null`, deliberately NOT IsNullOrEmpty.
            //
            // NULL here means "refresh from the beginning of time", so treating "" as null converted an
            // empty configuration value into a semantically much WIDER policy: every chunk, on every run
            // of the job, silently. Config binding, LoadFrom and JSON/env deserialisation all produce ""
            // where the author wrote nothing.
            //
            // Every neighbouring interval in this class -- endOffset, scheduleInterval,
            // compressAfterInterval, dropAfterInterval, the chunk interval -- already fails LOUDLY on an
            // empty string, because EscapeLiteral("") renders INTERVAL '' and PostgreSQL rejects it.
            // Measured on TimescaleDB 2.29.2: `INTERVAL ''` is
            // `invalid input syntax for type interval: ""`. This one now behaves like them.
            var startOffsetSql = startOffset == null
                ? "NULL"
                : $"INTERVAL '{SqlLiteral.EscapeLiteral(startOffset)}'";
            return $"SELECT add_continuous_aggregate_policy('{connector.RegclassLiteral(viewName)}', "
                 + $"start_offset => {startOffsetSql}, "
                 + $"end_offset => INTERVAL '{SqlLiteral.EscapeLiteral(endOffset)}', "
                 + $"schedule_interval => INTERVAL '{SqlLiteral.EscapeLiteral(scheduleInterval)}');";
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
