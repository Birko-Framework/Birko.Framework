using System;
using Birko.Configuration;
using Birko.Data.Models;
using Birko.Data.SQL.Stores;

namespace Birko.Data.SQL.MSSql.Stores
{
    /// <summary>
    /// SQL Server-specific settings.
    /// Adds MultipleActiveResultSets and TrustServerCertificate options.
    /// </summary>
    public class MSSqlSettings : SqlSettings, ILoadable<MSSqlSettings>
    {
        /// <summary>
        /// Gets or sets whether to enable Multiple Active Result Sets. Default is false.
        /// </summary>
        public bool MultipleActiveResultSets { get; set; } = false;

        /// <summary>
        /// Gets or sets whether to trust the server certificate. Default is false.
        /// Commonly needed for Azure SQL Database connections.
        /// </summary>
        public bool TrustServerCertificate { get; set; } = false;

        public MSSqlSettings() : base() { }

        public MSSqlSettings(string location, string name, string? username = null, string? password = null, int port = 1433, bool useSecure = true)
            : base(location, name, username, password, port, useSecure) { }

        public override string GetConnectionString()
        {
            return $"Server=tcp:{Location},{Port};Initial Catalog={Name};Persist Security Info=False;User ID={UserName};Password={Password};MultipleActiveResultSets={(MultipleActiveResultSets ? "True" : "False")};Encrypt={(UseSecure ? "True" : "False")};TrustServerCertificate={(TrustServerCertificate ? "True" : "False")};Connection Timeout={ConnectionTimeout};";
        }

        public void LoadFrom(MSSqlSettings data)
        {
            if (data != null)
            {
                base.LoadFrom((SqlSettings)data);
                MultipleActiveResultSets = data.MultipleActiveResultSets;
                TrustServerCertificate = data.TrustServerCertificate;
            }
        }

        public override void LoadFrom(Birko.Configuration.Settings data)
        {
            if (data is MSSqlSettings msData)
            {
                LoadFrom(msData);
            }
            else
            {
                base.LoadFrom(data);
            }
        }
    }
}
