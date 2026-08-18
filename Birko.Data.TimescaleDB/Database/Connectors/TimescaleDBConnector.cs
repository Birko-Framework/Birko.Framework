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
        internal static string BuildCreateHypertableSql(string tableName, string timeColumn, string chunkTimeInterval)
        {
            return string.Format(
                "SELECT create_hypertable({0}, {1}, chunk_time_interval => INTERVAL '{2}', if_not_exists => TRUE)",
                "'" + tableName.Replace("'", "''") + "'",
                "'" + timeColumn.Replace("'", "''") + "'",
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
