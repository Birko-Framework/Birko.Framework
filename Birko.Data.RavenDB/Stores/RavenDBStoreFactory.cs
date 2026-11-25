using System;
using Birko.Data.Stores;

namespace Birko.Data.RavenDB.Stores;

/// <summary>
/// The RavenDB <see cref="IStoreFactory"/> (TASK-990), chosen by the backend name <see cref="Name"/>. One store per type,
/// held by the factory: a store builds its own <c>DocumentStore</c> when given settings, so a store per call would be a
/// document store per call.
/// </summary>
public sealed class RavenDBStoreFactory : SettingsStoreFactory<Settings>
{
    /// <summary>The backend name this factory registers under.</summary>
    public const string Name = "RavenDB";

    public RavenDBStoreFactory(Settings settings) : base(Name, settings)
    {
        if (string.IsNullOrWhiteSpace(settings.Location) || string.IsNullOrWhiteSpace(settings.Name))
            throw new ArgumentException($"Store backend '{Name}' needs the server URL in Settings.Location and the database in Settings.Name.", nameof(settings));
    }

    /// <summary>
    /// <c>Url=http://host:8080;Database=app</c> (RavenDB's own form), both required. Any other key is refused: RavenDB
    /// authenticates with a client certificate, which a string cannot carry; configure it on a <c>DocumentStore</c> instead.
    /// </summary>
    public static Settings ParseConnectionString(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new ArgumentException($"Store backend '{Name}' needs Url=…;Database=….", nameof(connectionString));
        var keys = Birko.Data.Stores.StoreFactory.ParseKeys(Name, connectionString, "Url", "Database");
        if (!keys.TryGetValue("Url", out var url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            throw new ArgumentException($"Store backend '{Name}' needs an http(s) server address in Url=….", nameof(connectionString));
        if (!keys.TryGetValue("Database", out var database) || string.IsNullOrWhiteSpace(database))
            throw new ArgumentException($"Store backend '{Name}' needs Database=….", nameof(connectionString));
        return new Settings(url, database);
    }

    /// <inheritdoc />
    protected override IAsyncBulkStore<T> NewStore<T>()
    {
        var store = new AsyncRavenDBStore<T>();
        store.SetSettings(BackendSettings);
        return store;
    }
}

/// <summary>Registers RavenDB with <see cref="Birko.Data.Stores.StoreFactory"/> (TASK-990).</summary>
public static class RavenDBStoreProviders
{
    /// <summary>Adds the <c>RavenDB</c> backend: the connection string is <c>Url=…;Database=…</c>.</summary>
    public static StoreProviders AddRavenDB(this StoreProviders providers)
        => providers.Add<Settings>(RavenDBStoreFactory.Name, RavenDBStoreFactory.ParseConnectionString, s => new RavenDBStoreFactory(s));
}
