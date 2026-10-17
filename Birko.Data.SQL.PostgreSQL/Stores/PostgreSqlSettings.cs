using System;
using Birko.Configuration;
using Birko.Data.Models;
using Birko.Data.SQL.Stores;

namespace Birko.Data.SQL.PostgreSQL.Stores
{
    /// <summary>
    /// PostgreSQL-specific settings.
    /// Adds UseBinaryImport option for COPY protocol bulk operations.
    /// </summary>
    public class PostgreSqlSettings : SqlSettings, ILoadable<PostgreSqlSettings>
    {
        /// <summary>
        /// Gets or sets whether to use binary import (COPY protocol) for bulk operations. Default is true.
        /// </summary>
        public bool UseBinaryImport { get; set; } = true;

        public PostgreSqlSettings() : base() { }

        public PostgreSqlSettings(string location, string name, string? username = null, string? password = null, int port = 5432, bool useSecure = false)
            : base(location, name, username, password, port, useSecure) { }

        public override string GetConnectionString()
            => ComposeConnectionString(Location, Port, UserName, Password, Name, ConnectionTimeout, CommandTimeout, UseSecure);

        /// <summary>
        /// The one producer of a PostgreSQL connection string (TASK-653): <see cref="GetConnectionString"/>, the
        /// connector's plain-<see cref="RemoteSettings"/> path and TimescaleDB all compose through it.
        /// </summary>
        /// <remarks>
        /// Composed with <see cref="Npgsql.NpgsqlConnectionStringBuilder"/>, which quotes a value containing <c>;</c>,
        /// <c>=</c> or a quote, so a credential stays a value (CR-L189). Interpolated, a password ending
        /// <c>;Host=evil</c> moved the connection to another host and <c>;SSL Mode=Disable</c> dropped TLS (measured).
        /// A null timeout leaves its key out. Measured on TASK-653: the builder emits Port, Timeout and Command Timeout
        /// even at the Npgsql default, which CR-L189's note said it omitted.
        /// </remarks>
        public static string ComposeConnectionString(string? host, int port, string? userName, string? password, string? database,
            int? timeout, int? commandTimeout, bool useSecure)
        {
            var builder = new Npgsql.NpgsqlConnectionStringBuilder
            {
                Host = host,
                Port = port,
                Username = userName,
                Password = password,
                Database = database,
            };
            if (timeout.HasValue)
            {
                builder.Timeout = timeout.Value;
            }
            if (commandTimeout.HasValue)
            {
                builder.CommandTimeout = commandTimeout.Value;
            }
            if (useSecure)
            {
                builder.SslMode = Npgsql.SslMode.Require;
            }
            return builder.ConnectionString;
        }

        public void LoadFrom(PostgreSqlSettings data)
        {
            if (data != null)
            {
                base.LoadFrom((SqlSettings)data);
                UseBinaryImport = data.UseBinaryImport;
            }
        }

        public override void LoadFrom(Birko.Configuration.Settings data)
        {
            if (data is PostgreSqlSettings pgData)
            {
                LoadFrom(pgData);
            }
            else
            {
                base.LoadFrom(data);
            }
        }
    }
}
