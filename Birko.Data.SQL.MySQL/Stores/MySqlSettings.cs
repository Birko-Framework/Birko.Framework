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
        {
            var cs = $"Server={Location};Port={Port};User ID={UserName};Password={Password};Database={Name};Connection Timeout={ConnectionTimeout};";
            if (UseSecure)
            {
                cs += "SslMode=Required;";
            }
            return cs;
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
