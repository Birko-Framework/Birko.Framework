using System;
using System.Linq;
using Birko.Data.Stores;

namespace Birko.Data.MongoDB.Stores
{
    /// <summary>
    /// The MongoDB <see cref="IStoreFactory"/> (TASK-990), chosen by the backend name <see cref="Name"/>. One store per
    /// type, held by the factory: a store builds its own client when given settings, so a store per call would be a client
    /// per call.
    /// </summary>
    public sealed class MongoDBStoreFactory : SettingsStoreFactory<Settings>
    {
        /// <summary>The backend name this factory registers under.</summary>
        public const string Name = "MongoDB";

        public MongoDBStoreFactory(Settings settings) : base(Name, settings)
        {
            if (string.IsNullOrWhiteSpace(settings.Name))
                throw new ArgumentException($"Store backend '{Name}' needs a database name in Settings.Name.", nameof(settings));
        }

        /// <summary>
        /// MongoDB's own connection string, <c>mongodb://…/database</c> or <c>mongodb+srv://…/database</c>, used verbatim
        /// (every option it carries reaches the driver). The database is the one in its path and is required: the stores
        /// of one factory share it.
        /// </summary>
        public static Settings ParseConnectionString(string? connectionString)
        {
            if (string.IsNullOrWhiteSpace(connectionString)
                || !(connectionString.StartsWith("mongodb://", StringComparison.OrdinalIgnoreCase)
                     || connectionString.StartsWith("mongodb+srv://", StringComparison.OrdinalIgnoreCase)))
                throw new ArgumentException($"Store backend '{Name}' takes MongoDB's own connection string (mongodb://host/database).", nameof(connectionString));

            global::MongoDB.Driver.MongoUrl url;
            try { url = global::MongoDB.Driver.MongoUrl.Create(connectionString); }
            catch (Exception ex) when (ex is global::MongoDB.Driver.MongoConfigurationException or FormatException)
            {
                throw new ArgumentException($"Store backend '{Name}' could not read its connection string: {ex.Message}", nameof(connectionString), ex);
            }
            if (string.IsNullOrEmpty(url.DatabaseName))
                throw new ArgumentException($"Store backend '{Name}' needs the database in the connection string's path (mongodb://host/database).", nameof(connectionString));

            // Location and UserName feed GetId, which keys the cached client: two strings for different servers or users
            // must not share one.
            return new Settings
            {
                // MongoServerAddress.ToString() is the type name, not host:port: every string would get one Location.
                Location = string.Join(",", url.Servers.Select(s => $"{s.Host}:{s.Port}")),
                Name = url.DatabaseName,
                UserName = url.Username ?? string.Empty,
                RawConnectionString = connectionString,
            };
        }

        /// <inheritdoc />
        protected override IAsyncBulkStore<T> NewStore<T>()
        {
            var store = new AsyncMongoDBStore<T>();
            store.SetSettings(BackendSettings);
            return store;
        }
    }

    /// <summary>Registers MongoDB with <see cref="Birko.Data.Stores.StoreFactory"/> (TASK-990).</summary>
    public static class MongoDBStoreProviders
    {
        /// <summary>Adds the <c>MongoDB</c> backend: the connection string is <c>mongodb://…/database</c>.</summary>
        public static StoreProviders AddMongoDB(this StoreProviders providers)
            => providers.Add<Settings>(MongoDBStoreFactory.Name, MongoDBStoreFactory.ParseConnectionString, s => new MongoDBStoreFactory(s));
    }
}
