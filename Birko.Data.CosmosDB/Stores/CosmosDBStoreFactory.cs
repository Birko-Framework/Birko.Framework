using System;
using Birko.Data.Stores;
using Microsoft.Azure.Cosmos;

namespace Birko.Data.CosmosDB.Stores;

/// <summary>
/// The Cosmos DB <see cref="IStoreFactory"/> (TASK-990), chosen by the backend name <see cref="Name"/>. Each type is a
/// container named after it, as any Cosmos DB store without a container name does. One store per type, held by the
/// factory: a store builds its own <c>CosmosClient</c> when given settings, so a store per call would be a client per call.
/// </summary>
public sealed class CosmosDBStoreFactory : SettingsStoreFactory<Settings>
{
    /// <summary>The backend name this factory registers under.</summary>
    public const string Name = "CosmosDB";

    public CosmosDBStoreFactory(Settings settings) : base(Name, settings)
    {
        if (string.IsNullOrWhiteSpace(settings.Location) || string.IsNullOrWhiteSpace(settings.Name))
            throw new ArgumentException($"Store backend '{Name}' needs the account endpoint in Settings.Location and the database in Settings.Name.", nameof(settings));
        if (!string.IsNullOrWhiteSpace(settings.UserName))
            throw new ArgumentException(
                $"Store backend '{Name}' gives each type its own container; Settings.UserName (one container for every store) would put every type in one.",
                nameof(settings));
    }

    /// <summary>
    /// Cosmos DB's own connection string plus the database: <c>AccountEndpoint=…;AccountKey=…;Database=…</c>, and
    /// optionally <c>ConnectionMode=Gateway</c> (the emulator, a firewall) or <c>Direct</c> (the default). Any other key is
    /// refused.
    /// </summary>
    public static Settings ParseConnectionString(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new ArgumentException($"Store backend '{Name}' needs AccountEndpoint=…;AccountKey=…;Database=….", nameof(connectionString));
        var keys = Birko.Data.Stores.StoreFactory.ParseKeys(Name, connectionString, "AccountEndpoint", "AccountKey", "Database", "ConnectionMode");
        if (!keys.TryGetValue("AccountEndpoint", out var endpoint) || !Uri.TryCreate(endpoint, UriKind.Absolute, out _))
            throw new ArgumentException($"Store backend '{Name}' needs the account address in AccountEndpoint=….", nameof(connectionString));
        if (!keys.TryGetValue("AccountKey", out var key) || string.IsNullOrWhiteSpace(key))
            throw new ArgumentException($"Store backend '{Name}' needs AccountKey=….", nameof(connectionString));
        if (!keys.TryGetValue("Database", out var database) || string.IsNullOrWhiteSpace(database))
            throw new ArgumentException($"Store backend '{Name}' needs Database=….", nameof(connectionString));

        var settings = new Settings(endpoint, database, key);
        if (keys.TryGetValue("ConnectionMode", out var mode))
        {
            if (!Enum.TryParse<ConnectionMode>(mode, ignoreCase: true, out var parsed) || !Enum.IsDefined(parsed))
                throw new ArgumentException($"Store backend '{Name}': ConnectionMode must be Direct or Gateway.", nameof(connectionString));
            settings.ConnectionMode = parsed;
        }
        return settings;
    }

    /// <inheritdoc />
    protected override IAsyncBulkStore<T> NewStore<T>()
    {
        var store = new AsyncCosmosDBStore<T>();
        store.SetSettings(BackendSettings);
        return store;
    }
}

/// <summary>Registers Cosmos DB with <see cref="Birko.Data.Stores.StoreFactory"/> (TASK-990).</summary>
public static class CosmosDBStoreProviders
{
    /// <summary>Adds the <c>CosmosDB</c> backend: the connection string is <c>AccountEndpoint=…;AccountKey=…;Database=…</c>.</summary>
    public static StoreProviders AddCosmosDB(this StoreProviders providers)
        => providers.Add<Settings>(CosmosDBStoreFactory.Name, CosmosDBStoreFactory.ParseConnectionString, s => new CosmosDBStoreFactory(s));
}
