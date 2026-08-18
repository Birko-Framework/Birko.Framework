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

        /// <inheritdoc />
        public override void CreateTable(string name, IEnumerable<string> fields)
        {
            base.CreateTable(name, fields);

            if (_timescaleSettings != null && !string.IsNullOrEmpty(_timescaleSettings.TimeColumn))
            {
                CreateHypertable(name, _timescaleSettings.TimeColumn, _timescaleSettings.ChunkTimeInterval);
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
        /// Composes the <c>create_hypertable(...)</c> SQL. Extracted (CR-M177) so the single-quote
        /// escaping and INTERVAL formatting are covered without a live database, and shared by the
        /// sync and async paths.
        /// </summary>
        /// <remarks>
        /// <b>Both identifiers travel as string VALUES here, not as identifiers, and that is what was
        /// wrong with them (TASK-472).</b> Everywhere else this framework emits an identifier into
        /// <c>CommandText</c> the PostgreSQL parser folds it; inside a quoted literal the parser never
        /// sees an identifier at all, so the folding that makes the rest of the framework work does not
        /// happen and each argument needs the opposite treatment:
        /// <list type="bullet">
        /// <item>
        /// <description>
        /// <b>The table is a <c>regclass</c>, so it must carry its own quotes.</b> Emitted bare, the
        /// regclass lookup folds <c>'BulkTxRows'</c> to <c>bulktxrows</c> while
        /// <see cref="AbstractConnector.CreateTable(string, IEnumerable{string})"/> created
        /// <c>"BulkTxRows"</c> — so this raised <c>42P01 relation "bulktxrows" does not exist</c>, which
        /// <see cref="PostgreSQLConnector.IsMissingTableException"/> classifies as a missing table and the
        /// exception handler therefore <i>swallows</i>. Measured on TimescaleDB 2 / PostgreSQL 16:
        /// <c>CreateTable</c> returned successfully and <b>no hypertable existed</b> — so no
        /// PascalCase-named entity, which is every Birko entity by convention, has ever been a hypertable.
        /// Chunk routing, compression and retention were all silently absent while a plain PostgreSQL table
        /// served reads and writes and made the store look correct.
        /// </description>
        /// </item>
        /// <item>
        /// <description>
        /// <b>The time column is a <c>name</c>, so it must be pre-FOLDED — the opposite of quoting.</b> It
        /// is compared literally against <c>pg_attribute.attname</c>, and this framework emits column
        /// definitions bare (§ Conventions, TASK-209), so PostgreSQL stored <c>ts</c> while
        /// <c>TimeColumn = "Ts"</c> asked for <c>Ts</c>: <c>42703 column "Ts" does not exist</c>. That one
        /// is <i>not</i> swallowed, so it was the loud half — and it stayed hidden because the shipped
        /// default <c>TimeColumn</c> is the already-lowercase <c>"timestamp"</c>, which matches a folded
        /// <c>Timestamp</c> property by luck.
        /// </description>
        /// </item>
        /// </list>
        /// <para>
        /// Folding rather than quoting the column means a hand-made table whose time column was created
        /// <i>quoted</i> and mixed-case is not addressable through here. That is deliberate: this framework
        /// never quotes a column definition, so it cannot produce such a table, and the previous behaviour
        /// worked only for an already-lowercase name — folding is a strict improvement on every table
        /// Birko itself creates.
        /// </para>
        /// <para>
        /// Injection was already contained by the single-quote doubling and still is; the identifier quotes
        /// added to the table argument are doubled for the same reason.
        /// </para>
        /// </remarks>
        internal static string BuildCreateHypertableSql(string tableName, string timeColumn, string chunkTimeInterval)
        {
            // Quote as an identifier first (doubling any embedded quote), then escape the whole thing for
            // the surrounding string literal. Neither step can introduce the other's metacharacter.
            var quotedTable = "\"" + tableName.Replace("\"", "\"\"") + "\"";

            return string.Format(
                "SELECT create_hypertable({0}, {1}, chunk_time_interval => INTERVAL '{2}', if_not_exists => TRUE)",
                "'" + quotedTable.Replace("'", "''") + "'",
                "'" + timeColumn.ToLowerInvariant().Replace("'", "''") + "'",
                chunkTimeInterval.Replace("'", "''"));
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
        public async System.Threading.Tasks.Task CreateHypertableAsync(string tableName, string timeColumn, string chunkTimeInterval = "7 days", System.Threading.CancellationToken ct = default)
        {
            using var connection = (NpgsqlConnection)CreateConnection(_settings);
            await connection.OpenAsync(ct).ConfigureAwait(false);
            string? commandText = null;
            try
            {
                using var command = connection.CreateCommand();
                command.CommandText = BuildCreateHypertableSql(tableName, timeColumn, chunkTimeInterval);
                commandText = command.CommandText;
                await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }
            catch (System.OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                InitException(ex, commandText ?? "CreateHypertableAsync " + tableName);
            }
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
