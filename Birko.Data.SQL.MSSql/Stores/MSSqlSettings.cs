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

        /// <summary>
        /// The one producer of a SQL Server connection string; the connector's plain-<see cref="RemoteSettings"/> path
        /// derives an <see cref="MSSqlSettings"/> and comes here too (CR-M136).
        /// </summary>
        /// <remarks>
        /// TASK-653 (SH-M280): composed with <see cref="Microsoft.Data.SqlClient.SqlConnectionStringBuilder"/>, which
        /// quotes a value containing <c>;</c>, <c>=</c> or a quote. Interpolated, a password ending <c>;Server=evil</c>
        /// moved the connection to another host and <c>;Encrypt=False</c> dropped TLS (measured). The builder writes its
        /// own key spellings (<c>Data Source</c>, <c>Multiple Active Result Sets</c>, <c>Trust Server Certificate</c>,
        /// <c>Connect Timeout</c>); the keys and values are the ones the interpolated string set.
        /// </remarks>
        public override string GetConnectionString()
        {
            var builder = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder
            {
                DataSource = $"tcp:{Location},{Port}",
                PersistSecurityInfo = false,
                MultipleActiveResultSets = MultipleActiveResultSets,
                Encrypt = UseSecure,
                TrustServerCertificate = TrustServerCertificate,
                ConnectTimeout = ConnectionTimeout,
            };
            if (Name != null) builder.InitialCatalog = Name;
            if (UserName != null) builder.UserID = UserName;
            if (Password != null) builder.Password = Password;
            return builder.ConnectionString;
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
