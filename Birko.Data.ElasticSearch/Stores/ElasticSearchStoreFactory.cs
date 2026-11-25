using System;
using Birko.Data.Stores;

namespace Birko.Data.ElasticSearch.Stores
{
    /// <summary>
    /// The Elasticsearch <see cref="IStoreFactory"/> (TASK-990), chosen by the backend name <see cref="Name"/>. Each type
    /// is an index named <c>{IndexPrefix}_{type}</c>, as any Elasticsearch store names it. One store per type, held by the
    /// factory, like every non-SQL backend.
    /// </summary>
    public sealed class ElasticSearchStoreFactory : SettingsStoreFactory<Settings>
    {
        /// <summary>The backend name this factory registers under.</summary>
        public const string Name = "ElasticSearch";

        public ElasticSearchStoreFactory(Settings settings) : base(Name, settings)
        {
            if (string.IsNullOrWhiteSpace(settings.Location) || string.IsNullOrWhiteSpace(settings.Name))
                throw new ArgumentException(
                    $"Store backend '{Name}' needs the server URL in Settings.Location and the index prefix in Settings.Name.", nameof(settings));
        }

        /// <summary>
        /// <c>Url=http://host:9200;IndexPrefix=app</c>, both required. Any other key is refused: the settings carry no
        /// credentials or client options, so one given would be dropped.
        /// </summary>
        public static Settings ParseConnectionString(string? connectionString)
        {
            if (string.IsNullOrWhiteSpace(connectionString))
                throw new ArgumentException($"Store backend '{Name}' needs Url=…;IndexPrefix=….", nameof(connectionString));
            var keys = Birko.Data.Stores.StoreFactory.ParseKeys(Name, connectionString, "Url", "IndexPrefix");
            if (!keys.TryGetValue("Url", out var url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri)
                || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                throw new ArgumentException($"Store backend '{Name}' needs an http(s) server address in Url=….", nameof(connectionString));
            if (!keys.TryGetValue("IndexPrefix", out var prefix) || string.IsNullOrWhiteSpace(prefix))
                throw new ArgumentException($"Store backend '{Name}' needs IndexPrefix=… (indexes are named {{prefix}}_{{type}}).", nameof(connectionString));
            return new Settings { Location = url, Name = prefix };
        }

        /// <inheritdoc />
        protected override IAsyncBulkStore<T> NewStore<T>()
        {
            var store = new AsyncElasticSearchStore<T>();
            store.SetSettings(BackendSettings);
            return store;
        }
    }

    /// <summary>Registers Elasticsearch with <see cref="Birko.Data.Stores.StoreFactory"/> (TASK-990).</summary>
    public static class ElasticSearchStoreProviders
    {
        /// <summary>Adds the <c>ElasticSearch</c> backend: the connection string is <c>Url=…;IndexPrefix=…</c>.</summary>
        public static StoreProviders AddElasticSearch(this StoreProviders providers)
            => providers.Add<Settings>(ElasticSearchStoreFactory.Name, ElasticSearchStoreFactory.ParseConnectionString, s => new ElasticSearchStoreFactory(s));
    }
}
