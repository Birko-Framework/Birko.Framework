using System;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.Stores;

namespace Birko.Data.SQL.MSSql.Stores
{
    /// <summary>The SQL Server <see cref="ISqlStoreFactory"/> (TASK-988), chosen by the provider name <see cref="Name"/>.</summary>
    public sealed class MSSqlSqlStoreFactory : SqlStoreFactory<MSSqlConnector>
    {
        /// <summary>The provider name this factory registers under.</summary>
        public const string Name = "MSSql";

        public MSSqlSqlStoreFactory(MSSqlSettings settings) : base(Name, settings)
        {
            MSSqlSettings = settings;
        }

        /// <summary>The settings, as their SQL Server type.</summary>
        public MSSqlSettings MSSqlSettings { get; }

        /// <summary>
        /// A SqlClient connection string: Data Source (<c>host</c>, <c>host,port</c>, optionally <c>tcp:</c>), Initial
        /// Catalog, User ID, Password, Encrypt (<c>True</c> / <c>False</c>), Trust Server Certificate, Multiple Active
        /// Result Sets and Connect Timeout, in any of SqlClient's spellings. Any other key (Integrated Security, a named
        /// instance, Encrypt=Strict, …) is refused: <see cref="MSSqlSettings"/> cannot carry it.
        /// </summary>
        public static MSSqlSqlStoreFactory FromConnectionString(string connectionString)
        {
            SqlStoreFactory.RequireOnlyKeys(Name, connectionString,
                "Data Source", "DataSource", "Server", "Address", "Addr", "Network Address", "Initial Catalog", "Database",
                "User ID", "UserID", "UID", "User", "Password", "PWD", "Encrypt", "Trust Server Certificate",
                "TrustServerCertificate", "Multiple Active Result Sets", "MultipleActiveResultSets", "Connect Timeout",
                "Connection Timeout", "ConnectTimeout", "Timeout", "Persist Security Info", "PersistSecurityInfo");
            var b = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(connectionString);
            var source = b.DataSource?.Trim() ?? string.Empty;
            if (source.StartsWith("tcp:", StringComparison.OrdinalIgnoreCase)) source = source.Substring(4);
            if (source.Length == 0) throw new ArgumentException("A SQL Server connection string needs Data Source.", nameof(connectionString));
            if (source.Contains('\\'))
                throw new ArgumentException($"A SQL Server named instance ('{source}') cannot be expressed: give host,port.", nameof(connectionString));
            if (b.PersistSecurityInfo) throw new ArgumentException("Persist Security Info=True cannot be expressed.", nameof(connectionString));
            var host = source;
            var port = 1433;
            var comma = source.LastIndexOf(',');
            if (comma >= 0)
            {
                host = source.Substring(0, comma).Trim();
                if (!int.TryParse(source.Substring(comma + 1).Trim(), out port))
                    throw new ArgumentException($"SQL Server port in '{b.DataSource}' is not a number.", nameof(connectionString));
            }
            bool useSecure;
            if (b.Encrypt == Microsoft.Data.SqlClient.SqlConnectionEncryptOption.Mandatory) useSecure = true;
            else if (b.Encrypt == Microsoft.Data.SqlClient.SqlConnectionEncryptOption.Optional) useSecure = false;
            else throw new ArgumentException($"SQL Server Encrypt '{b.Encrypt}' cannot be expressed: use True or False.", nameof(connectionString));

            return new MSSqlSqlStoreFactory(new MSSqlSettings(host, b.InitialCatalog ?? string.Empty,
                string.IsNullOrEmpty(b.UserID) ? null : b.UserID, string.IsNullOrEmpty(b.Password) ? null : b.Password, port, useSecure)
            {
                TrustServerCertificate = b.TrustServerCertificate,
                MultipleActiveResultSets = b.MultipleActiveResultSets,
                ConnectionTimeout = b.ConnectTimeout,
            });
        }

        /// <inheritdoc />
        protected override AsyncDataBaseBulkStore<MSSqlConnector, T> NewStore<T>()
        {
            var store = new AsyncMSSqlStore<T>();
            store.SetSettings(MSSqlSettings);
            return store;
        }
    }

    /// <summary>Registers SQL Server with <see cref="SqlStoreFactory.Create"/>.</summary>
    public static class MSSqlSqlStoreProviders
    {
        /// <summary>Adds the <c>MSSql</c> provider: the connection string is a SqlClient one.</summary>
        public static SqlStoreProviders AddMSSql(this SqlStoreProviders providers)
            => providers.Add(MSSqlSqlStoreFactory.Name, MSSqlSqlStoreFactory.FromConnectionString);
    }
}
