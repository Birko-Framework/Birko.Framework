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
        {
            // Compose via NpgsqlConnectionStringBuilder so values (Host/Username/Password/Database)
            // containing ';' or '=' are quoted/escaped correctly rather than breaking the key=value
            // parsing or injecting extra keywords (CR-L189). Note: the builder omits keys whose value
            // equals the Npgsql default (e.g. Port 5432, Timeout 15).
            var builder = new Npgsql.NpgsqlConnectionStringBuilder
            {
                Host = Location,
                Port = Port,
                Username = UserName,
                Password = Password,
                Database = Name,
                Timeout = ConnectionTimeout,
                CommandTimeout = CommandTimeout,
            };
            if (UseSecure)
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
