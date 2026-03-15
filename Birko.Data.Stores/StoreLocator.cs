using System;
using System.Collections.Generic;
using System.Text;

namespace Birko.Data.Stores
{
    public static class StoreLocator
    {
        private static readonly object _lockObject = new();
        private static IDictionary<string, IDictionary<Type, object>>? _stores;

        public static TStore GetStore<TStore>()
            where TStore : IBaseStore

        {
            return GetStore<TStore, ISettings>(default!);
        }

        public static TStore GetStore<TStore, TSettings>(TSettings settings)
            where TSettings : ISettings
            where TStore: IBaseStore

        {
            var id = settings?.GetId() ?? string.Empty;
            var type = typeof(TStore);
            lock (_lockObject)
            {
                _stores ??= new Dictionary<string, IDictionary<Type, object>>();
                if (!_stores.ContainsKey(id))
                {
                    _stores.Add(id, new Dictionary<Type, object>());
                }

                if (!_stores[id].ContainsKey(type))
                {
                    _stores[id].Add(type, (TStore)Activator.CreateInstance(type, new object[] { })!);
                    if (!string.IsNullOrEmpty(id) && _stores[id][type] is ISettingsStore<ISettings> settingsStore)
                    {
                        settingsStore.SetSettings(settings!);
                    }
                }
            }
            return (TStore)_stores[id][type];
        }
    }
}
