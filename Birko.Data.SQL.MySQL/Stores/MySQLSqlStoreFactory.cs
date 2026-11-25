using System;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.Stores;

namespace Birko.Data.SQL.MySQL.Stores
{
    /// <summary>The MySQL <see cref="ISqlStoreFactory"/> (TASK-988), chosen by the provider name <see cref="Name"/>.</summary>
    public sealed class MySQLSqlStoreFactory : SqlStoreFactory<MySQLConnector>
    {
        /// <summary>The provider name this factory registers under.</summary>
        public const string Name = "MySQL";

        public MySQLSqlStoreFactory(MySqlSettings settings) : base(Name, settings)
        {
            MySqlSettings = settings;
        }

        /// <summary>The settings, as their MySQL type.</summary>
        public MySqlSettings MySqlSettings { get; }

        /// <summary>
        /// A MySqlConnector connection string: Server, Port, Database, User ID, Password, Connection Timeout and SSL Mode
        /// (<c>Required</c>, or <c>Preferred</c> / absent), in any of its spellings. Any other key, or another SSL mode, is
        /// refused: <see cref="MySqlSettings"/> cannot carry it.
        /// </summary>
        public static MySQLSqlStoreFactory FromConnectionString(string connectionString)
        {
            SqlStoreFactory.RequireOnlyKeys(Name, connectionString,
                "Server", "Host", "Data Source", "DataSource", "Address", "Addr", "Network Address", "Port", "Database",
                "Initial Catalog", "User ID", "UserID", "User", "Username", "UID", "User Name", "Password", "PWD",
                "Connection Timeout", "Connect Timeout", "ConnectionTimeout", "SSL Mode", "SslMode");
            var b = new MySqlConnector.MySqlConnectionStringBuilder(connectionString);
            if (string.IsNullOrWhiteSpace(b.Server)) throw new ArgumentException("A MySQL connection string needs Server.", nameof(connectionString));
            var useSecure = b.SslMode switch
            {
                MySqlConnector.MySqlSslMode.Required => true,
                MySqlConnector.MySqlSslMode.Preferred => false,
                _ => throw new ArgumentException(
                    $"MySQL SSL Mode '{b.SslMode}' cannot be expressed: use Required, or leave it out (Preferred).", nameof(connectionString)),
            };
            return new MySQLSqlStoreFactory(new MySqlSettings(b.Server, b.Database ?? string.Empty, b.UserID, b.Password, (int)b.Port, useSecure)
            {
                ConnectionTimeout = (int)b.ConnectionTimeout,
            });
        }

        /// <inheritdoc />
        protected override AsyncDataBaseBulkStore<MySQLConnector, T> NewStore<T>()
        {
            var store = new AsyncMySQLStore<T>();
            store.SetSettings(MySqlSettings);
            return store;
        }
    }

    /// <summary>Registers MySQL with <see cref="SqlStoreFactory.Create"/>.</summary>
    public static class MySQLSqlStoreProviders
    {
        /// <summary>Adds the <c>MySQL</c> provider: the connection string is a MySqlConnector one.</summary>
        public static SqlStoreProviders AddMySQL(this SqlStoreProviders providers)
            => providers.Add(MySQLSqlStoreFactory.Name, MySQLSqlStoreFactory.FromConnectionString);
    }
}
