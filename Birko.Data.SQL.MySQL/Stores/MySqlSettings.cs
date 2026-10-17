using System;
using Birko.Configuration;
using Birko.Data.Models;
using Birko.Data.SQL.Stores;

namespace Birko.Data.SQL.MySQL.Stores
{
    /// <summary>
    /// MySQL-specific settings.
    /// Adds BulkInsertBatchSize configuration.
    /// </summary>
    public class MySqlSettings : SqlSettings, ILoadable<MySqlSettings>
    {
        /// <summary>
        /// Gets or sets the batch size for bulk insert operations. Default is 1000.
        /// Limited by MySQL's 65535 total parameter limit.
        /// </summary>
        public int BulkInsertBatchSize { get; set; } = 1000;

        public MySqlSettings() : base() { }

        public MySqlSettings(string location, string name, string? username = null, string? password = null, int port = 3306, bool useSecure = false)
            : base(location, name, username, password, port, useSecure) { }

        public override string GetConnectionString()
            => ComposeConnectionString(Location, Port, UserName, Password, Name, ConnectionTimeout, UseSecure);

        /// <summary>
        /// The one producer of a MySQL connection string (TASK-653, SH-M280): <see cref="GetConnectionString"/> and the
        /// connector's plain-<see cref="RemoteSettings"/> path compose through it.
        /// </summary>
        /// <remarks>
        /// Composed with <see cref="MySqlConnector.MySqlConnectionStringBuilder"/>, which quotes a value containing
        /// <c>;</c>, <c>=</c> or a quote. Interpolated, a password ending <c>;Server=evil</c> moved the connection to
        /// another host and <c>;SslMode=None</c> dropped TLS (measured). A null timeout leaves the key out.
        /// </remarks>
        public static string ComposeConnectionString(string? server, int port, string? userName, string? password, string? database,
            int? connectionTimeout, bool useSecure)
        {
            var builder = new MySqlConnector.MySqlConnectionStringBuilder
            {
                Port = (uint)port,
            };
            if (server != null) builder.Server = server;
            if (userName != null) builder.UserID = userName;
            if (password != null) builder.Password = password;
            if (database != null) builder.Database = database;
            if (connectionTimeout.HasValue)
            {
                builder.ConnectionTimeout = (uint)connectionTimeout.Value;
            }
            if (useSecure)
            {
                builder.SslMode = MySqlConnector.MySqlSslMode.Required;
            }
            return builder.ConnectionString;
        }

        public void LoadFrom(MySqlSettings data)
        {
            if (data != null)
            {
                base.LoadFrom((SqlSettings)data);
                BulkInsertBatchSize = data.BulkInsertBatchSize;
            }
        }

        public override void LoadFrom(Birko.Configuration.Settings data)
        {
            if (data is MySqlSettings myData)
            {
                LoadFrom(myData);
            }
            else
            {
                base.LoadFrom(data);
            }
        }
    }
}
