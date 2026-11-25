using System;
using System.IO;
using Birko.Data.Models;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.Stores;

namespace Birko.Data.SQL.SqLite.Stores
{
    /// <summary>
    /// The SQLite <see cref="ISqlStoreFactory"/> (TASK-988), chosen by the provider name <see cref="Name"/>. Stores use
    /// <see cref="SqLiteSettings"/>, so a database gets WAL on first use as any SQLite store does (TASK-941), and the
    /// file path is made absolute so one file always maps to one cached connector, however the path was written.
    /// </summary>
    public sealed class SqLiteSqlStoreFactory : SqlStoreFactory<SqLiteConnector>
    {
        /// <summary>The provider name this factory registers under.</summary>
        public const string Name = "SqLite";

        public SqLiteSqlStoreFactory(SqLiteSettings settings) : base(Name, settings)
        {
            SqLiteSettings = settings;
            // SQLite creates the database file on demand, but not its folder.
            if (!string.IsNullOrEmpty(settings.Location)) Directory.CreateDirectory(settings.Location);
        }

        /// <summary>The settings, as their SQLite type.</summary>
        public SqLiteSettings SqLiteSettings { get; }

        /// <summary>
        /// A database file path (<c>data/app.db</c>), or a connection string with <c>Data Source</c> (or <c>Filename</c>)
        /// and optionally <c>Password</c>. Any other key is refused: <see cref="SqLiteSettings"/> cannot carry it.
        /// </summary>
        public static SqLiteSqlStoreFactory FromConnectionString(string connectionString)
        {
            if (string.IsNullOrWhiteSpace(connectionString)) throw new ArgumentException("A SQLite file path is required.", nameof(connectionString));
            string path;
            string? password = null;
            if (connectionString.Contains('='))
            {
                SqlStoreFactory.RequireOnlyKeys(Name, connectionString, "Data Source", "DataSource", "Filename", "Password");
                var builder = new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder(connectionString);
                path = builder.DataSource;
                if (!string.IsNullOrEmpty(builder.Password)) password = builder.Password;
            }
            else
            {
                path = connectionString.Trim();
            }
            if (string.IsNullOrWhiteSpace(path) || string.IsNullOrEmpty(Path.GetFileName(path)))
                throw new ArgumentException($"SQLite needs a database file path; got '{connectionString}'.", nameof(connectionString));

            var full = Path.GetFullPath(path);
            return new SqLiteSqlStoreFactory(new SqLiteSettings(Path.GetDirectoryName(full) ?? string.Empty, Path.GetFileName(full), password));
        }

        /// <inheritdoc />
        protected override AsyncDataBaseBulkStore<SqLiteConnector, T> NewStore<T>()
        {
            var store = new AsyncSQLiteStore<T>();
            store.SetSettings(SqLiteSettings);
            return store;
        }
    }

    /// <summary>Registers SQLite with <see cref="SqlStoreFactory.Create"/>.</summary>
    public static class SqLiteSqlStoreProviders
    {
        /// <summary>Adds the <c>SqLite</c> provider: the connection string is a file path or <c>Data Source=…</c>.</summary>
        public static SqlStoreProviders AddSqLite(this SqlStoreProviders providers)
            => providers.Add(SqLiteSqlStoreFactory.Name, SqLiteSqlStoreFactory.FromConnectionString);

        /// <summary>
        /// Adds this provider to the backend-neutral registry (TASK-990), so <see cref="Birko.Data.Stores.StoreFactory"/>
        /// can choose it beside the NoSQL backends: the connection string is a file path or <c>Data Source=…</c>, as for <see cref="SqlStoreFactory"/>.
        /// </summary>
        public static Birko.Data.Stores.StoreProviders AddSqLite(this Birko.Data.Stores.StoreProviders providers)
            => providers.Add<SqLiteSettings>(SqLiteSqlStoreFactory.Name,
                cs => SqLiteSqlStoreFactory.FromConnectionString(SqlStoreFactory.RequireConnectionString(SqLiteSqlStoreFactory.Name, cs)).SqLiteSettings,
                settings => new SqLiteSqlStoreFactory(settings));
    }
}
