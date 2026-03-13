using System;
using System.Collections.Generic;
using System.Linq;
using Birko.Data.SQL.Connectors;
using Npgsql;
using RemoteSettings = Birko.Data.Stores.RemoteSettings;
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
        public TimescaleDBConnector(TimescaleDBSettings settings) : base(settings)
        {
            _timescaleSettings = settings;
        }

        /// <summary>
        /// Initializes a new instance of the TimescaleDBConnector class with remote settings.
        /// Uses default TimescaleDB hypertable settings.
        /// </summary>
        /// <param name="settings">The remote settings for connection.</param>
        public TimescaleDBConnector(RemoteSettings settings) : base(settings)
        {
            if (settings is TimescaleDBSettings timescaleSettings)
            {
                _timescaleSettings = timescaleSettings;
            }
            else
            {
                _timescaleSettings = new TimescaleDBSettings
                {
                    Location = settings.Location,
                    Name = settings.Name,
                    Password = settings.Password,
                    UserName = settings.UserName,
                    Port = settings.Port,
                    UseSsl = settings.UseSsl
                };
            }
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
            DoCommand((command) =>
            {
                command.CommandText = string.Format(
                    "SELECT create_hypertable({0}, {1}, chunk_time_interval => INTERVAL '{2}', if_not_exists => TRUE)",
                    "'" + tableName.Replace("'", "''") + "'",
                    "'" + timeColumn.Replace("'", "''") + "'",
                    chunkTimeInterval.Replace("'", "''"));
            }, (command) =>
            {
                command.ExecuteNonQuery();
            }, true);
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
                command.CommandText = string.Format(
                    "SELECT create_hypertable({0}, {1}, chunk_time_interval => INTERVAL '{2}', if_not_exists => TRUE)",
                    "'" + tableName.Replace("'", "''") + "'",
                    "'" + timeColumn.Replace("'", "''") + "'",
                    chunkTimeInterval.Replace("'", "''"));
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
