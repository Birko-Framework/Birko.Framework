using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Birko.Configuration;
using Birko.Data.Models;

namespace Birko.Data.Stores
{
    /// <summary>
    /// The backends an app can choose between (TASK-990). This project references no backend; each registers itself with
    /// its own extension (<c>AddSqLite()</c>, <c>AddPostgreSQL()</c>, <c>AddMongoDB()</c>, <c>AddInMemory()</c>, …), so an
    /// app carries only the drivers it registers.
    /// </summary>
    public sealed class StoreProviders
    {
        private sealed record Entry(string Name, Type SettingsType, Func<string?, ISettings> Parse, Func<ISettings, IStoreFactory> Create);

        private readonly Dictionary<string, Entry> _entries = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>The registered backend names.</summary>
        public IReadOnlyCollection<string> Names => _entries.Keys;

        /// <summary>
        /// Registers <paramref name="name"/> (matched ignoring case): <paramref name="parse"/> turns a connection string
        /// into the backend's settings, refusing what they cannot carry, and <paramref name="create"/> builds the factory
        /// from settings. A name registered twice is refused: two meanings for one configuration value is a configuration
        /// error.
        /// </summary>
        public StoreProviders Add<TSettings>(string name, Func<string?, TSettings> parse, Func<TSettings, IStoreFactory> create)
            where TSettings : class, ISettings
        {
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("A backend name is required.", nameof(name));
            if (parse is null) throw new ArgumentNullException(nameof(parse));
            if (create is null) throw new ArgumentNullException(nameof(create));
            if (_entries.ContainsKey(name)) throw new ArgumentException($"Store backend '{name}' is already registered.", nameof(name));
            _entries[name] = new Entry(name, typeof(TSettings), cs => parse(cs), s => create((TSettings)s));
            return this;
        }

        internal IStoreFactory? CreateFromString(string name, string? connectionString)
            => _entries.TryGetValue(name, out var e) ? e.Create(e.Parse(connectionString)) : null;

        /// <summary>
        /// The backend registered for <paramref name="settings"/>' type: the registration whose settings type it is, else
        /// the single one whose backend settings type it derives from. Two that could take it are refused, naming them,
        /// rather than one picked.
        /// </summary>
        /// <remarks>
        /// A registration on one of the shared base types in Birko.Configuration (<c>Settings</c>, <c>PasswordSettings</c>,
        /// <c>RemoteSettings</c>: InMemory, JSON, XML) matches only that exact type. Every backend's settings derive from
        /// them, so matching by derivation sent a RavenDB <c>Settings</c> to InMemory (measured, TASK-990).
        /// </remarks>
        internal IStoreFactory CreateFromSettings(ISettings settings)
        {
            var type = settings.GetType();
            var exact = _entries.Values.Where(e => e.SettingsType == type).ToList();
            var match = exact.Count > 0
                ? exact
                : _entries.Values.Where(e => e.SettingsType.Namespace != typeof(ISettings).Namespace && e.SettingsType.IsAssignableFrom(type)).ToList();
            if (match.Count == 1) return match[0].Create(settings);
            if (match.Count == 0)
                throw new ArgumentException(
                    $"No registered store backend takes {type.FullName} (registered: {Known()}).", nameof(settings));
            throw new ArgumentException(
                $"{type.FullName} could configure more than one registered backend ({string.Join(", ", match.Select(e => e.Name))}); "
                + "pass the backend's own settings type, or use StoreFactory.Create(backend, connectionString, …).", nameof(settings));
        }

        internal string Known() => _entries.Count == 0 ? "none" : string.Join(", ", _entries.Keys.OrderBy(n => n, StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>Builds an <see cref="IStoreFactory"/> for the backend a configuration names (TASK-990).</summary>
    public static class StoreFactory
    {
        /// <summary>
        /// The factory for <paramref name="backend"/> on <paramref name="connectionString"/>, choosing among the backends
        /// <paramref name="providers"/> registers. Refused by name: a backend that is not registered, and a connection
        /// string the backend cannot use (each backend says which strings it takes; InMemory takes none). Register the
        /// result once, e.g.
        /// <c>services.AddSingleton(StoreFactory.Create(cfg.Backend, cfg.ConnectionString, p => p.AddInMemory().AddMongoDB()))</c>.
        /// </summary>
        public static IStoreFactory Create(string backend, string? connectionString, Action<StoreProviders> providers)
        {
            if (providers is null) throw new ArgumentNullException(nameof(providers));
            if (string.IsNullOrWhiteSpace(backend)) throw new ArgumentException("A store backend name is required.", nameof(backend));
            var registry = new StoreProviders();
            providers(registry);
            return registry.CreateFromString(backend.Trim(), connectionString)
                ?? throw new ArgumentException($"Store backend '{backend}' is not registered (registered: {registry.Known()}).", nameof(backend));
        }

        /// <summary>
        /// The factory for the backend whose settings <paramref name="settings"/> are (a MongoDB <c>Settings</c> picks
        /// MongoDB), for a consumer that builds the settings object itself rather than a connection string.
        /// </summary>
        public static IStoreFactory Create(ISettings settings, Action<StoreProviders> providers)
        {
            if (settings is null) throw new ArgumentNullException(nameof(settings));
            if (providers is null) throw new ArgumentNullException(nameof(providers));
            var registry = new StoreProviders();
            providers(registry);
            return registry.CreateFromSettings(settings);
        }

        /// <summary>
        /// Parses a <c>Key=Value;…</c> connection string, refusing a key outside <paramref name="allowed"/> (compared
        /// ignoring case, spaces and underscores) by name: a backend's settings carry only some keys, and one they would
        /// drop is refused rather than silently lost (rule 41). Keys come back under their <paramref name="allowed"/>
        /// spelling.
        /// </summary>
        public static IReadOnlyDictionary<string, string> ParseKeys(string backend, string connectionString, params string[] allowed)
        {
            static string Norm(string k) => k.Replace(" ", "").Replace("_", "").ToLowerInvariant();
            var byNorm = allowed.ToDictionary(Norm, a => a);
            var parsed = new System.Data.Common.DbConnectionStringBuilder { ConnectionString = connectionString };
            var unknown = parsed.Keys.Cast<string>().Where(k => !byNorm.ContainsKey(Norm(k))).ToList();
            if (unknown.Count > 0)
            {
                throw new ArgumentException(
                    $"Store backend '{backend}' cannot use connection string key(s) {string.Join(", ", unknown.Select(k => $"'{k}'"))}: "
                    + $"its settings carry only {string.Join(", ", allowed)}.", nameof(connectionString));
            }
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string key in parsed.Keys) result[byNorm[Norm(key)]] = Convert.ToString(parsed[key]) ?? string.Empty;
            return result;
        }
    }

    /// <summary>
    /// The shared half of a NoSQL / file / memory backend's <see cref="IStoreFactory"/> (TASK-990): a backend supplies its
    /// settings and how to build one store, and the factory answers the <b>same store for a type every time</b>. Those
    /// stores hold what must not be made per call: InMemory, JSON and XML hold the data itself (two instances would not
    /// see each other's writes), and MongoDB, RavenDB and Cosmos DB each build their own client (<c>MongoClient</c>,
    /// <c>DocumentStore</c>, <c>CosmosClient</c>) when given settings.
    /// </summary>
    public abstract class SettingsStoreFactory<TSettings> : IStoreFactory
        where TSettings : class, ISettings
    {
        private readonly ConcurrentDictionary<Type, Lazy<object>> _stores = new();

        protected SettingsStoreFactory(string backend, TSettings settings)
        {
            Backend = backend ?? throw new ArgumentNullException(nameof(backend));
            BackendSettings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        /// <inheritdoc />
        public string Backend { get; }

        /// <summary>The settings, as the backend's own type.</summary>
        public TSettings BackendSettings { get; }

        /// <inheritdoc />
        public ISettings Settings => BackendSettings;

        /// <summary>A new store for <typeparamref name="T"/> with <see cref="BackendSettings"/> set; called once per type.</summary>
        protected abstract IAsyncBulkStore<T> NewStore<T>() where T : AbstractModel;

        /// <inheritdoc />
        public IAsyncBulkStore<T> GetAsyncStore<T>() where T : AbstractModel
            => (IAsyncBulkStore<T>)_stores.GetOrAdd(typeof(T), _ => new Lazy<object>(() => NewStore<T>())).Value;
    }
}
