using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Birko.Configuration;

namespace Birko.Data.Stores
{
    /// <summary>
    /// A process-wide cache of store instances, one per <c>(settings.GetId(), store type)</c>. A store is created with
    /// its parameterless constructor and configured once, with the settings of the call that created it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Serves sync (<see cref="IBaseStore"/>) and async (<see cref="IAsyncBaseStore"/>) stores alike — SH-M304: the
    /// type parameter used to be constrained to <see cref="IBaseStore"/>, which the async family does not implement,
    /// so no async store could be located at all. The check is now made at run time.
    /// </para>
    /// <para>
    /// SH-M305: a cache hit with settings that share the id but differ in a value property (a password, a token, an
    /// auth database — fields several providers leave out of <see cref="ISettings.GetId"/>) <b>throws</b>. It used to
    /// return the store configured with the first caller's settings, so the second caller silently ran on someone
    /// else's credentials. Equal values in a fresh settings instance are a hit, as before.
    /// </para>
    /// </remarks>
    public static class StoreLocator
    {
        private static readonly object _lockObject = new();
        private static IDictionary<string, IDictionary<Type, Entry>>? _stores;

        private sealed class Entry
        {
            public Entry(object store, ISettings? settings)
            {
                Store = store;
                Settings = settings;
            }

            public object Store { get; }
            public ISettings? Settings { get; }
        }

        public static TStore GetStore<TStore>()
        {
            return GetStore<TStore, ISettings>(default!);
        }

        public static TStore GetStore<TStore, TSettings>(TSettings settings)
            where TSettings : ISettings
        {
            var type = typeof(TStore);
            if (!typeof(IBaseStore).IsAssignableFrom(type) && !typeof(IAsyncBaseStore).IsAssignableFrom(type))
            {
                throw new ArgumentException(
                    $"{type.FullName} is not a store: StoreLocator serves types implementing {nameof(IBaseStore)} or {nameof(IAsyncBaseStore)}.",
                    nameof(TStore));
            }

            var id = settings?.GetId() ?? string.Empty;
            lock (_lockObject)
            {
                _stores ??= new Dictionary<string, IDictionary<Type, Entry>>();
                if (!_stores.TryGetValue(id, out var byType))
                {
                    byType = new Dictionary<Type, Entry>();
                    _stores.Add(id, byType);
                }

                if (byType.TryGetValue(type, out var cached))
                {
                    if (!string.IsNullOrEmpty(id) && !SameValues(cached.Settings, settings))
                    {
                        throw new InvalidOperationException(
                            $"StoreLocator already holds a {type.Name} for settings id '{id}', configured with different " +
                            $"settings. {settings!.GetType().Name}.GetId() does not distinguish them, so the cached store " +
                            "would run with the first caller's values. Construct this store directly, or use settings whose " +
                            "GetId() differs.");
                    }
                    // Read inside the lock (SH-M303, as RepositoryLocator's CR-H081): the maps are plain
                    // Dictionaries, and an unlocked read can race a concurrent writer's resize.
                    return (TStore)cached.Store;
                }

                var store = Activator.CreateInstance(type, Array.Empty<object>())!;
                if (!string.IsNullOrEmpty(id) && store is ISettingsStore<ISettings> settingsStore)
                {
                    settingsStore.SetSettings(settings!);
                }
                byType.Add(type, new Entry(store, string.IsNullOrEmpty(id) ? null : settings));
                return (TStore)store;
            }
        }

        /// <summary>
        /// Two settings carry the same values: same runtime type, and every public readable instance property of a
        /// value-like type (primitives, enums, strings, <see cref="decimal"/>, <see cref="TimeSpan"/>,
        /// <see cref="DateTime"/>, <see cref="Guid"/>, and their nullable forms) is equal. Properties of other types
        /// cannot be compared by value and are not compared.
        /// </summary>
        private static bool SameValues(ISettings? cached, ISettings? requested)
        {
            if (ReferenceEquals(cached, requested)) return true;
            if (cached == null || requested == null) return false;
            if (cached.GetType() != requested.GetType()) return false;

            return cached.GetType()
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.CanRead && p.GetIndexParameters().Length == 0 && IsValueLike(p.PropertyType))
                .All(p => Equals(p.GetValue(cached), p.GetValue(requested)));
        }

        private static bool IsValueLike(Type type)
        {
            type = Nullable.GetUnderlyingType(type) ?? type;
            return type.IsPrimitive || type.IsEnum || type == typeof(string) || type == typeof(decimal)
                || type == typeof(TimeSpan) || type == typeof(DateTime) || type == typeof(DateTimeOffset) || type == typeof(Guid);
        }
    }
}
