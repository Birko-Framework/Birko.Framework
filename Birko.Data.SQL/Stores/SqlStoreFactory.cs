using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Birko.Configuration;
using Birko.Data.Models;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.Repositories;
using Birko.Data.Stores;

namespace Birko.Data.SQL.Stores
{
    /// <summary>
    /// The SQL providers an app can choose between by name (TASK-988). This project does not reference the provider
    /// projects; each adds itself with its own extension (<c>AddSqLite()</c>, <c>AddPostgreSQL()</c>, <c>AddMySQL()</c>,
    /// <c>AddMSSql()</c>), so an app carries only the drivers it registers.
    /// </summary>
    public sealed class SqlStoreProviders
    {
        private readonly Dictionary<string, Func<string, ISqlStoreFactory>> _providers = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>The registered provider names.</summary>
        public IReadOnlyCollection<string> Names => _providers.Keys;

        /// <summary>
        /// Registers <paramref name="name"/> (matched ignoring case) with what builds its factory from a connection
        /// string. A name registered twice is refused: two meanings for one configuration value is a configuration error.
        /// </summary>
        public SqlStoreProviders Add(string name, Func<string, ISqlStoreFactory> create)
        {
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("A provider name is required.", nameof(name));
            if (create is null) throw new ArgumentNullException(nameof(create));
            if (_providers.ContainsKey(name)) throw new ArgumentException($"SQL provider '{name}' is already registered.", nameof(name));
            _providers[name] = create;
            return this;
        }

        internal Func<string, ISqlStoreFactory>? Find(string name) => _providers.TryGetValue(name, out var create) ? create : null;
    }

    /// <summary>Builds an <see cref="ISqlStoreFactory"/> for the provider a configuration names (TASK-988).</summary>
    public static class SqlStoreFactory
    {
        /// <summary>
        /// The factory for <paramref name="provider"/> on <paramref name="connectionString"/>, choosing among the providers
        /// <paramref name="providers"/> registers. Refused by name: a provider that is not registered, an empty connection
        /// string, and anything the provider cannot use in it. Register the result once, e.g.
        /// <c>services.AddSingleton(SqlStoreFactory.Create(cfg.Provider, cfg.ConnectionString, p => p.AddSqLite().AddPostgreSQL()))</c>.
        /// </summary>
        public static ISqlStoreFactory Create(string provider, string connectionString, Action<SqlStoreProviders> providers)
        {
            if (providers is null) throw new ArgumentNullException(nameof(providers));
            if (string.IsNullOrWhiteSpace(provider)) throw new ArgumentException("A SQL provider name is required.", nameof(provider));
            if (string.IsNullOrWhiteSpace(connectionString))
                throw new ArgumentException($"A connection string is required for SQL provider '{provider}'.", nameof(connectionString));

            var registry = new SqlStoreProviders();
            providers(registry);
            var create = registry.Find(provider.Trim());
            if (create is null)
            {
                var known = registry.Names.Count == 0 ? "none" : string.Join(", ", registry.Names.OrderBy(n => n, StringComparer.OrdinalIgnoreCase));
                throw new ArgumentException($"SQL provider '{provider}' is not registered (registered: {known}).", nameof(provider));
            }
            return create(connectionString);
        }

        /// <summary>
        /// <paramref name="connectionString"/>, or a refusal naming <paramref name="provider"/> when it is empty: what a
        /// provider's registration with the shared <see cref="StoreProviders"/> checks first (TASK-990), as
        /// <see cref="Create"/> does for <see cref="SqlStoreProviders"/>.
        /// </summary>
        public static string RequireConnectionString(string provider, string? connectionString)
            => string.IsNullOrWhiteSpace(connectionString)
                ? throw new ArgumentException($"A connection string is required for SQL provider '{provider}'.", nameof(connectionString))
                : connectionString;

        /// <summary>
        /// Refuses a connection string with a key outside <paramref name="allowed"/> (compared ignoring case, spaces and
        /// underscores), naming the keys. A provider's settings can express only some of its driver's keys; one it would
        /// drop (pooling, a search path, integrated security, …) is refused rather than silently lost (rule 41).
        /// </summary>
        public static void RequireOnlyKeys(string provider, string connectionString, params string[] allowed)
        {
            static string Norm(string k) => k.Replace(" ", "").Replace("_", "").ToLowerInvariant();
            var ok = new HashSet<string>(allowed.Select(Norm));
            var parsed = new System.Data.Common.DbConnectionStringBuilder { ConnectionString = connectionString };
            var unknown = parsed.Keys.Cast<string>().Where(k => !ok.Contains(Norm(k))).ToList();
            if (unknown.Count > 0)
            {
                throw new ArgumentException(
                    $"SQL provider '{provider}' cannot use connection string key(s) {string.Join(", ", unknown.Select(k => $"'{k}'"))}: "
                    + $"its settings carry only {string.Join(", ", allowed)}.", nameof(connectionString));
            }
        }
    }

    /// <summary>
    /// The shared half of a provider's <see cref="ISqlStoreFactory"/>: the connector, the repository and schema creation.
    /// A provider supplies its store type and its settings (TASK-988).
    /// </summary>
    public abstract class SqlStoreFactory<TConnector> : ISqlStoreFactory
        where TConnector : AbstractConnector
    {
        protected SqlStoreFactory(string provider, PasswordSettings settings)
        {
            Provider = provider ?? throw new ArgumentNullException(nameof(provider));
            Settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        /// <inheritdoc />
        public string Provider { get; }

        string IStoreFactory.Backend => Provider;

        ISettings IStoreFactory.Settings => Settings;

        /// <inheritdoc />
        public PasswordSettings Settings { get; }

        /// <inheritdoc />
        public AbstractConnector GetConnector() => DataBase.GetConnector<TConnector>(Settings);

        /// <summary>A new provider store with <see cref="Settings"/> set.</summary>
        protected abstract AsyncDataBaseBulkStore<TConnector, T> NewStore<T>() where T : AbstractModel;

        /// <inheritdoc />
        public IAsyncSqlBulkStore<T> GetAsyncStore<T>() where T : AbstractModel => NewStore<T>();

        IAsyncBulkStore<T> IStoreFactory.GetAsyncStore<T>() => GetAsyncStore<T>();

        /// <inheritdoc />
        public AsyncSqlModelRepository<T> GetAsyncRepository<T>() where T : AbstractModel => new(GetAsyncStore<T>());

        /// <inheritdoc />
        public Task CreateSchemaAsync(IEnumerable<Type> types, CancellationToken ct = default)
        {
            if (types is null) throw new ArgumentNullException(nameof(types));
            var list = types.ToArray();
            var connector = GetConnector();
            return Task.Run(() => connector.CreateTable(list), ct);
        }
    }
}
