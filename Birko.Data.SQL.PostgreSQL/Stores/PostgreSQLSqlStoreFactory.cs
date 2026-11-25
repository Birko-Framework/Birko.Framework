using System;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.Stores;

namespace Birko.Data.SQL.PostgreSQL.Stores
{
    /// <summary>The PostgreSQL <see cref="ISqlStoreFactory"/> (TASK-988), chosen by the provider name <see cref="Name"/>.</summary>
    public sealed class PostgreSQLSqlStoreFactory : SqlStoreFactory<PostgreSQLConnector>
    {
        /// <summary>The provider name this factory registers under.</summary>
        public const string Name = "PostgreSQL";

        public PostgreSQLSqlStoreFactory(PostgreSqlSettings settings) : base(Name, settings)
        {
            PostgreSqlSettings = settings;
        }

        /// <summary>The settings, as their PostgreSQL type.</summary>
        public PostgreSqlSettings PostgreSqlSettings { get; }

        /// <summary>
        /// An Npgsql connection string: Host, Port, Database, Username, Password, Timeout, Command Timeout and SSL Mode
        /// (<c>Require</c>, or <c>Prefer</c> / absent), in any of Npgsql's spellings. Any other key, or another SSL mode, is
        /// refused: <see cref="PostgreSqlSettings"/> cannot carry it, and dropping it would change the connection.
        /// </summary>
        public static PostgreSQLSqlStoreFactory FromConnectionString(string connectionString)
        {
            SqlStoreFactory.RequireOnlyKeys(Name, connectionString,
                "Host", "Server", "Port", "Database", "DB", "Username", "User Name", "User Id", "UserId", "User", "UID",
                "Password", "PSW", "PWD", "Timeout", "Command Timeout", "CommandTimeout", "SSL Mode", "SslMode");
            var b = new Npgsql.NpgsqlConnectionStringBuilder(connectionString);
            if (string.IsNullOrWhiteSpace(b.Host)) throw new ArgumentException("A PostgreSQL connection string needs Host.", nameof(connectionString));
            var useSecure = b.SslMode switch
            {
                Npgsql.SslMode.Require => true,
                Npgsql.SslMode.Prefer => false,
                _ => throw new ArgumentException(
                    $"PostgreSQL SSL Mode '{b.SslMode}' cannot be expressed: use Require, or leave it out (Prefer).", nameof(connectionString)),
            };
            return new PostgreSQLSqlStoreFactory(new PostgreSqlSettings(b.Host!, b.Database ?? string.Empty, b.Username, b.Password, b.Port, useSecure)
            {
                ConnectionTimeout = b.Timeout,
                CommandTimeout = b.CommandTimeout,
            });
        }

        /// <inheritdoc />
        protected override AsyncDataBaseBulkStore<PostgreSQLConnector, T> NewStore<T>()
        {
            var store = new AsyncPostgreSQLStore<T>();
            store.SetSettings(PostgreSqlSettings);
            return store;
        }
    }

    /// <summary>Registers PostgreSQL with <see cref="SqlStoreFactory.Create"/>.</summary>
    public static class PostgreSQLSqlStoreProviders
    {
        /// <summary>Adds the <c>PostgreSQL</c> provider: the connection string is an Npgsql one.</summary>
        public static SqlStoreProviders AddPostgreSQL(this SqlStoreProviders providers)
            => providers.Add(PostgreSQLSqlStoreFactory.Name, PostgreSQLSqlStoreFactory.FromConnectionString);

        /// <summary>
        /// Adds this provider to the backend-neutral registry (TASK-990), so <see cref="Birko.Data.Stores.StoreFactory"/>
        /// can choose it beside the NoSQL backends: the connection string is a PostgreSQL connection string, as for <see cref="SqlStoreFactory"/>.
        /// </summary>
        public static Birko.Data.Stores.StoreProviders AddPostgreSQL(this Birko.Data.Stores.StoreProviders providers)
            => providers.Add<PostgreSqlSettings>(PostgreSQLSqlStoreFactory.Name,
                cs => PostgreSQLSqlStoreFactory.FromConnectionString(SqlStoreFactory.RequireConnectionString(PostgreSQLSqlStoreFactory.Name, cs)).PostgreSqlSettings,
                settings => new PostgreSQLSqlStoreFactory(settings));
    }
}
