using System;
using System.Collections.Generic;
using System.Linq;
using Birko.Data.SQL.Connectors;
using Npgsql;
using RemoteSettings = Birko.Configuration.RemoteSettings;
using TimescaleDBSettings = Birko.Data.SQL.TimescaleDB.Stores.TimescaleDBSettings;

namespace Birko.Data.SQL.Connectors
{
    /// <summary>
    /// A table whose hypertable conversion schema-ensure could not perform. The table exists and is fully
    /// usable as a plain PostgreSQL table; only the partitioning is absent (TASK-254).
    /// </summary>
    /// <remarks>
    /// Deliberately its own type rather than an <see cref="IndexCreationFailure"/> carrying a sentinel index
    /// name: that collection is public surface a consumer reads and documents, and injecting foreign entries
    /// into it would change what their existing checks see.
    /// </remarks>
    public sealed class HypertableCreationFailure
    {
        public HypertableCreationFailure(string tableName, string timeColumn, Exception error)
        {
            TableName = tableName;
            TimeColumn = timeColumn;
            Error = error;
        }

        public string TableName { get; }

        /// <summary>The time column the conversion was attempted with — from settings, not from the entity.</summary>
        public string TimeColumn { get; }

        public Exception Error { get; }

        public override string ToString()
            => $"hypertable conversion of '{TableName}' on time column '{TimeColumn}': {Error.Message}";
    }

    /// <summary>
    /// TimescaleDB database connector.
    /// Extends PostgreSQLConnector with hypertable creation support.
    /// </summary>
    public class TimescaleDBConnector : PostgreSQLConnector
    {
        private readonly TimescaleDBSettings _timescaleSettings;

        /// <summary>
        /// Initializes a new instance of the TimescaleDBConnector class.
        /// </summary>
        /// <param name="settings">The TimescaleDB settings for connection.</param>
        public TimescaleDBConnector(TimescaleDBSettings settings)
            : base(settings ?? throw new ArgumentNullException(nameof(settings)))
        {
            _timescaleSettings = settings;
        }

        /// <summary>
        /// Initializes a new instance of the TimescaleDBConnector class with remote settings.
        /// Uses default TimescaleDB hypertable settings.
        /// </summary>
        /// <param name="settings">The remote settings for connection.</param>
        /// <remarks>
        /// CR-M176: chains to the typed constructor via <see cref="AsTimescaleSettings"/> so the base
        /// <c>_settings</c> (used by CreateConnection / bulk ops) and <c>_timescaleSettings</c> are the
        /// SAME TimescaleDBSettings instance. Previously this called <c>base(settings)</c> with the raw
        /// RemoteSettings, so <c>_settings</c> was a RemoteSettings that never reached
        /// <c>TimescaleDBSettings.GetConnectionString()</c> — CreateConnection silently dropped the
        /// TimescaleDB connection-string semantics on the RemoteSettings path.
        /// </remarks>
        public TimescaleDBConnector(RemoteSettings settings) : this(AsTimescaleSettings(settings))
        {
        }

        /// <summary>
        /// Returns the settings as a TimescaleDBSettings — the same instance when already one, otherwise
        /// a new TimescaleDBSettings carrying the connection fields (hypertable defaults applied).
        /// </summary>
        private static TimescaleDBSettings AsTimescaleSettings(RemoteSettings settings)
        {
            // CR-L232: fail with a clear ArgumentNullException instead of NRE-ing on settings.Location.
            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            if (settings is TimescaleDBSettings timescaleSettings)
            {
                return timescaleSettings;
            }

            return new TimescaleDBSettings
            {
                Location = settings.Location,
                Name = settings.Name,
                Password = settings.Password,
                UserName = settings.UserName,
                Port = settings.Port,
                UseSecure = settings.UseSecure
            };
        }

        /// <summary>
        /// Honors <see cref="TimescaleDBSettings.GetConnectionString"/> (CommandTimeout /
        /// ConnectionTimeout / SSL). The base PostgreSQLConnector only calls GetConnectionString for
        /// a <c>PostgreSqlSettings</c>; a TimescaleDBSettings is a sibling, not a PostgreSqlSettings,
        /// so it fell through to the generic branch that dropped those timeouts entirely (CR-H109).
        /// </summary>
        public override System.Data.Common.DbConnection CreateConnection(Birko.Configuration.PasswordSettings settings)
        {
            if (settings is TimescaleDBSettings tsSettings
                && !string.IsNullOrEmpty(tsSettings.Location) && !string.IsNullOrEmpty(tsSettings.Name))
            {
                return new NpgsqlConnection(tsSettings.GetConnectionString());
            }

            return base.CreateConnection(settings);
        }

        // TASK-254. Same bookkeeping as the index channel and deliberately the same helper: keyed by table,
        // current state rather than a log, event on the TRANSITION into failure, cleared when the conversion
        // later succeeds. Connectors are cached process-wide while _initialized lives on the store, so a
        // scoped store re-runs schema-ensure per request against this one object -- an append-only list here
        // would grow forever, which is the regression TASK-204 shipped and then fixed for indexes.
        private readonly SchemaEnsureFailureLog<HypertableCreationFailure> _hypertableFailures =
            new(f => f.TableName);

        /// <summary>
        /// Tables whose hypertable conversion could not be performed on their most recent schema-ensure
        /// attempt. Empty in the normal case.
        /// </summary>
        /// <remarks>
        /// Current state, not history: a table that later converts successfully drops out, and a given table
        /// appears at most once however many times schema-ensure has run. <b>An empty collection is NOT proof
        /// that every table is a hypertable</b> — stores initialise lazily, so an entity that has not been
        /// touched has not attempted its conversion. Same caveat the index channel carries.
        /// </remarks>
        public IReadOnlyList<HypertableCreationFailure> HypertableCreationFailures => _hypertableFailures.Snapshot;

        /// <summary>
        /// Raised when a table could not be converted into a hypertable during schema-ensure. Subscribe to
        /// log or escalate; the store initialises regardless and the table remains usable as a plain
        /// PostgreSQL table.
        /// </summary>
        /// <remarks>
        /// Fires on the TRANSITION into failure, not on every attempt.
        /// </remarks>
        public event Action<HypertableCreationFailure>? OnHypertableCreationFailed;

        /// <inheritdoc />
        /// <remarks>
        /// <b>A conversion that cannot be performed DEGRADES here; it does not throw</b> (TASK-254). This is
        /// the lazy schema-ensure path — stores set <c>_initialized</c> only after it returns, so an escaping
        /// exception leaves the entity's whole surface, <i>reads included</i>, throwing on every subsequent
        /// operation. That is exactly the failure mode TASK-204 removed for unbuildable indexes:
        /// <i>"lazy schema-ensure degrades and reports; an explicit schema call throws"</i>. The explicit
        /// door is <see cref="CreateHypertable(string, string, string)"/>, which still throws.
        /// <para>
        /// <b>Degrading is legitimate because nothing declares an entity to be a hypertable.</b> The
        /// conversion is applied to every table this connector creates whenever
        /// <c>TimescaleDBSettings.TimeColumn</c> is set, and there is no per-entity attribute — so a failure
        /// is a connector-wide default that did not apply, not a broken per-entity contract. TASK-204's rule
        /// is to degrade a constraint or an optimisation, never correctness, and partitioning is the former.
        /// </para>
        /// <para>
        /// <b>It rests on a measured premise — and that premise holds on ONE path only, which is why the
        /// catch is conditional.</b> On the own-connection path <c>base.CreateTable</c> has already committed
        /// the table when the conversion fails, so what remains is a fully usable plain PostgreSQL table
        /// (measured on TimescaleDB 2.29.2 / PostgreSQL 16.15 — written and read back after a <c>TS103</c>).
        /// <b>Inside a caller's ambient boundary it does not hold</b>: the <c>CREATE TABLE</c> is not
        /// committed, the failed statement aborts the transaction, and the table is gone — measured, 0 rows
        /// in <c>pg_tables</c> after a failed <c>create_hypertable</c> in a <c>BEGIN</c> block. Degrading
        /// there would leave the store initialised over a table that does not exist and cost the caller the
        /// real error, so the boundary path <b>rethrows</b>.
        /// <para>
        /// The first version of this change degraded unconditionally, with this paragraph stating the premise
        /// without its qualifier — the measurement had been taken on a bare connector with no boundary. That
        /// is worth remembering: <b>a premise measured on one path is not a premise, it is a sample.</b>
        /// TASK-244 made the boundary path reachable here by having <c>InitCore</c> enter the ambient scope.
        /// </para>
        /// </para>
        /// </remarks>
        public override void CreateTable(string name, IEnumerable<string> fields)
        {
            base.CreateTable(name, fields);

            if (_timescaleSettings == null || string.IsNullOrEmpty(_timescaleSettings.TimeColumn))
            {
                return;
            }

            try
            {
                CreateHypertable(name, _timescaleSettings.TimeColumn, _timescaleSettings.ChunkTimeInterval);
                // Cleared on success so the channel cannot report a condition that has been repaired -- e.g.
                // the offending unique index was dropped and the next schema-ensure converted the table.
                _hypertableFailures.Clear(name);
            }
            catch (Exception ex) when (AmbientTransaction == null)
            {
                // Degrade ONLY on the own-connection path, because that is the only path where the premise
                // holds. Inside a caller's boundary the CREATE TABLE above is not committed, PostgreSQL puts
                // the transaction into the aborted state, and swallowing would report success over a table
                // that will not exist -- measured: after a failed create_hypertable inside a BEGIN block the
                // table is gone (0 rows in pg_tables) and every later command in that transaction fails with
                // 25P02, naming neither TS103 nor this table. The caller would lose the real error entirely.
                //
                // So the `when` clause is load-bearing, not defensive: without it this fix is a regression on
                // exactly the path TASK-244 made reachable by having InitCore enter the ambient scope. Found
                // by code-review at TASK-254's close gate, against a remark two paragraphs above that had
                // already written down why it would be worse -- the premise was measured only on the
                // own-connection path.
                RecordHypertableCreationFailure(name, _timescaleSettings.TimeColumn, ex);
            }
        }

        /// <summary>
        /// Records a table schema-ensure could not convert, and notifies any subscriber the first time that
        /// table enters the failed state.
        /// </summary>
        /// <remarks>
        /// Deliberately does NOT rethrow — see <see cref="CreateTable(string, IEnumerable{string})"/> for why
        /// an unconvertible hypertable must not take the table's whole read surface with it.
        /// </remarks>
        private void RecordHypertableCreationFailure(string tableName, string timeColumn, Exception error)
        {
            var failure = new HypertableCreationFailure(tableName, timeColumn, error);
            if (!_hypertableFailures.Record(tableName, failure))
            {
                return;
            }

            try
            {
                OnHypertableCreationFailed?.Invoke(failure);
            }
            catch
            {
                // A subscriber that throws must NOT defeat the degrade. This invoke runs inside
                // CreateTable's catch, so an escaping handler exception would propagate out of schema-ensure
                // and leave the store permanently uninitialised -- precisely the failure TASK-254 exists to
                // remove, reintroduced through the reporting channel. The summary on the event invites a host
                // to "log or escalate", and escalating by rethrowing is the realistic trigger.
                //
                // Swallowed rather than recorded anywhere: the caller asked to be told about a degraded
                // conversion, and their own handler failing is their concern, not a second schema failure.
                // Found by code-review at TASK-254's close gate.
                //
                // The index channel (AbstractConnector.RecordIndexCreationFailure) has the identical hole and
                // is deliberately NOT changed here: it has real consumers, so altering whether a handler's
                // exception propagates is a behaviour change on consumed surface and wants its own
                // measurement. TASK-283 owns it.
            }
        }

        /// <summary>
        /// Converts a regular PostgreSQL table into a TimescaleDB hypertable.
        /// </summary>
        /// <param name="tableName">The name of the table to convert.</param>
        /// <param name="timeColumn">The time column to partition by.</param>
        /// <param name="chunkTimeInterval">The chunk time interval (e.g. "7 days").</param>
        public void CreateHypertable(string tableName, string timeColumn, string chunkTimeInterval = "7 days")
        {
            // DoDdlCommand, not DoCommand: on a provider whose DDL is not transactional this must not run
            // on an ambient boundary's connection, because the statement would implicitly commit it
            // (TASK-243). inOwnTransaction: false keeps this emitter autocommitted exactly as it was.
            DoDdlCommand((command) =>
            {
                command.CommandText = BuildCreateHypertableSql(tableName, timeColumn, chunkTimeInterval);
            }, (command) =>
            {
                command.ExecuteNonQuery();
            }, true, inOwnTransaction: false);
        }

        /// <summary>
        /// Composes the <c>create_hypertable(...)</c> SQL. Extracted (CR-M177) so the escaping and INTERVAL
        /// formatting are covered without a live database, and shared by the sync and async paths.
        /// </summary>
        /// <remarks>
        /// <b>Both identifiers travel as string VALUES here, not as identifiers, and that is what was wrong
        /// with them (TASK-472).</b> The rule itself now lives on the base — see
        /// <see cref="AbstractConnectorBase.RegclassLiteral"/> for the table and
        /// <see cref="AbstractConnectorBase.CatalogueNameLiteral"/> for the time column, which need
        /// <i>opposite</i> treatments and are the reason those two producers exist (TASK-253). This method is
        /// deliberately no longer the place that states it: it was, and the same statement written a second
        /// time in <c>Birko.Data.Migrations.TimescaleDB</c> is how the defect survived a layer over.
        /// <para>
        /// <b>What was measured here</b>, kept because it is this provider's evidence rather than the general
        /// rule. On TimescaleDB 2 / PostgreSQL 16 the table was emitted bare, so the regclass folded
        /// <c>'BulkTxRows'</c> to <c>bulktxrows</c> against the <c>"BulkTxRows"</c> that
        /// <see cref="AbstractConnector.CreateTable(string, IEnumerable{string})"/> had created —
        /// <c>42P01</c>, which <see cref="PostgreSQLConnector.IsMissingTableException"/> classifies as a
        /// missing table, so the handler <i>swallowed</i> it: <c>CreateTable</c> reported success and
        /// <b>no hypertable existed</b> for any PascalCase-named entity, which is every Birko entity by
        /// convention. Chunk routing, compression and retention were all silently absent while a plain
        /// PostgreSQL table served reads and writes and made the store look correct. The time column failed
        /// loudly instead (<c>42703 column "Ts" does not exist</c>) and stayed hidden only because the
        /// shipped default <c>TimeColumn</c> is the already-lowercase <c>"timestamp"</c>, matching a folded
        /// <c>Timestamp</c> property by luck.
        /// </para>
        /// <para>
        /// <b>Why this is an instance method.</b> It has to reach the two producers, which consult
        /// <see cref="AbstractConnectorBase.FoldsUnquotedIdentifiers"/> and
        /// <see cref="AbstractConnectorBase.QuoteIdentifier"/> — both provider state. The alternative was an
        /// optional-connector parameter defaulting to PostgreSQL's answers, which would have kept the four
        /// existing fixtures calling it statically; that is exactly the dead fallback branch TASK-247 deleted
        /// from <c>SqlSchemaBuilder</c> after finding every test in that project took it.
        /// </para>
        /// <para>
        /// Folding rather than quoting the column means a hand-made table whose time column was created
        /// <i>quoted</i> and mixed-case is not addressable through here. That is deliberate: this framework
        /// never quotes a column definition, so it cannot produce such a table, and the previous behaviour
        /// worked only for an already-lowercase name — folding is a strict improvement on every table Birko
        /// itself creates.
        /// </para>
        /// </remarks>
        internal string BuildCreateHypertableSql(string tableName, string timeColumn, string chunkTimeInterval)
        {
            // An absent interval OMITS the argument rather than emitting INTERVAL '' -- TimescaleDB then
            // applies its own default, which is what a caller who supplied no interval means. Matches the
            // sibling emitter TimescaleDBMigration.BuildCreateHypertableSql, which has always done this.
            //
            // It matters more since TASK-254 made schema-ensure degrade: EscapeLiteral refuses null and
            // INTERVAL '' is 22007, so before that a blank ChunkTimeInterval failed loudly out of CreateTable,
            // and afterwards it would be caught and recorded -- meaning NO table on the connector ever became
            // a hypertable and nothing surfaced unless the consumer had subscribed to the event. The value is
            // reachable: TimescaleDBSettings.ChunkTimeInterval is a public setter, also fed by the 7-arg
            // constructor and by LoadFrom. Found by code-review at TASK-254's close gate.
            var chunkIntervalSql = string.IsNullOrEmpty(chunkTimeInterval)
                ? string.Empty
                : $", chunk_time_interval => INTERVAL '{SqlLiteral.EscapeLiteral(chunkTimeInterval)}'";

            return string.Format(
                "SELECT create_hypertable({0}, {1}{2}, if_not_exists => TRUE)",
                "'" + RegclassLiteral(tableName) + "'",
                "'" + CatalogueNameLiteral(timeColumn) + "'",
                chunkIntervalSql);
        }

        /// <summary>
        /// Converts a regular PostgreSQL table into a TimescaleDB hypertable.
        /// </summary>
        /// <param name="type">The model type whose table to convert.</param>
        /// <param name="timeColumn">The time column to partition by.</param>
        /// <param name="chunkTimeInterval">The chunk time interval (e.g. "7 days").</param>
        public void CreateHypertable(Type type, string timeColumn, string chunkTimeInterval = "7 days")
        {
            var table = DataBase.LoadTable(type);
            if (table != null)
            {
                CreateHypertable(table.Name, timeColumn, chunkTimeInterval);
            }
        }

        /// <summary>
        /// Asynchronously converts a regular PostgreSQL table into a TimescaleDB hypertable.
        /// </summary>
        /// <param name="tableName">The name of the table to convert.</param>
        /// <param name="timeColumn">The time column to partition by.</param>
        /// <param name="chunkTimeInterval">The chunk time interval (e.g. "7 days").</param>
        /// <param name="ct">Cancellation token.</param>
        /// <remarks>
        /// <b>Goes through <c>DoDdlCommandAsync</c>, which the sync twin has done since TASK-243 and this did
        /// not.</b> It opened its own <c>CreateConnection</c> + <c>OpenAsync</c>, so it neither joined an
        /// ambient boundary nor was suppressed off one — it simply ran a second connection alongside, which on
        /// PostgreSQL is perfectly legal and therefore silent. A caller who wrapped this in a transaction got a
        /// hypertable conversion that survived their rollback, with no error either way. That is TASK-242's
        /// defect in a method the sweep did not reach, because it is not a bulk path.
        /// <para>
        /// <c>inOwnTransaction: false</c> mirrors the sync twin exactly, keeping the emitter autocommitted when
        /// it owns the connection. <see cref="PostgreSQLConnector.SupportsTransactionalDdl"/> is true, so
        /// nothing is suppressed and the statement joins the boundary rather than escaping it.
        /// </para>
        /// <para>
        /// <b>Why the cancellation guard survives the move.</b> The funnel routes every failure through
        /// <c>InitException</c>, and <c>PostgreSQLConnector</c> registers an <c>OnException</c> handler that
        /// re-throws as <c>new Exception(commandText, ex)</c> — so a cancellation would reach the caller as a
        /// bare <see cref="Exception"/> and <c>catch (OperationCanceledException)</c> would stop working. Every
        /// other async DDL path on this provider already behaves that way, so this method was the outlier;
        /// keeping its contract is deliberate rather than inconsistent, because its three public callers
        /// (<c>AsyncTimescaleDBStore</c>, <c>AsyncTimescaleDBModelRepository</c>,
        /// <c>AsyncTimescaleDBRepository</c>) take a token and a token that cannot be observed is worse than an
        /// inconsistency. <see cref="System.Runtime.ExceptionServices.ExceptionDispatchInfo"/> preserves the
        /// original stack rather than re-throwing a fresh one.
        /// </para>
        /// </remarks>
        public async System.Threading.Tasks.Task CreateHypertableAsync(string tableName, string timeColumn, string chunkTimeInterval = "7 days", System.Threading.CancellationToken ct = default)
        {
            try
            {
                await DoDdlCommandAsync((command) =>
                {
                    command.CommandText = BuildCreateHypertableSql(tableName, timeColumn, chunkTimeInterval);
                    return System.Threading.Tasks.Task.CompletedTask;
                }, (command) => command.ExecuteNonQueryAsync(ct), true, ct, inOwnTransaction: false).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not System.OperationCanceledException
                                       && ct.IsCancellationRequested
                                       && FindCancellation(ex) != null)
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(FindCancellation(ex)!).Throw();
            }
        }

        /// <summary>
        /// Walks <see cref="Exception.InnerException"/> to the first <see cref="System.OperationCanceledException"/>,
        /// or null if there is none.
        /// </summary>
        /// <remarks>
        /// <b>Walks the chain rather than checking one level, and the caller pairs it with
        /// <c>ct.IsCancellationRequested</c>.</b> Both halves close a hole found at TASK-253's close-gate review:
        /// <list type="bullet">
        /// <item><description>
        /// <c>OnException</c> is a <b>public event</b> and consumers subscribe to it. A handler that wraps the
        /// connector's own wrapper puts the cancellation at depth 2 or deeper, so a one-level check silently
        /// stops matching and the caller's <c>catch (OperationCanceledException)</c> breaks — the exact failure
        /// this guard exists to prevent, reappearing only under a consumer's configuration.
        /// </description></item>
        /// <item><description>
        /// Without the token check, an <see cref="System.OperationCanceledException"/> raised inside the try by
        /// something <i>else</i> — an <c>OnExecute</c> subscriber's own timeout, say — would be re-thrown as
        /// this call's cancellation. That is a wrong answer, not a lost one: a caller writing
        /// <c>catch (OperationCanceledException) when (ct.IsCancellationRequested)</c> would see a cancellation
        /// that never happened.
        /// </description></item>
        /// </list>
        /// </remarks>
        private static System.OperationCanceledException? FindCancellation(Exception? ex)
        {
            for (var current = ex; current != null; current = current.InnerException)
            {
                if (current is System.OperationCanceledException oce)
                {
                    return oce;
                }
            }
            return null;
        }

        /// <summary>
        /// Asynchronously converts a regular PostgreSQL table into a TimescaleDB hypertable.
        /// </summary>
        /// <param name="type">The model type whose table to convert.</param>
        /// <param name="timeColumn">The time column to partition by.</param>
        /// <param name="chunkTimeInterval">The chunk time interval (e.g. "7 days").</param>
        /// <param name="ct">Cancellation token.</param>
        public async System.Threading.Tasks.Task CreateHypertableAsync(Type type, string timeColumn, string chunkTimeInterval = "7 days", System.Threading.CancellationToken ct = default)
        {
            var table = DataBase.LoadTable(type);
            if (table != null)
            {
                await CreateHypertableAsync(table.Name, timeColumn, chunkTimeInterval, ct).ConfigureAwait(false);
            }
        }
    }
}
