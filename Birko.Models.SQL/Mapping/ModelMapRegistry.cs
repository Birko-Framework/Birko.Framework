using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Birko.Models.SQL.Mapping
{
    /// <summary>
    /// Central registry for model-to-SQL mappings. Discovers and caches
    /// IModelMapping implementations from assemblies.
    /// </summary>
    public class ModelMapRegistry
    {
        private readonly Dictionary<Type, object> _maps = new Dictionary<Type, object>();

        /// <summary>
        /// Register a single mapping.
        /// </summary>
        public void Register<T>(IModelMapping<T> mapping) where T : class
        {
            var map = new ModelMap<T>();
            mapping.Configure(map);
            _maps[typeof(T)] = map;
        }

        /// <summary>
        /// Scan an assembly for all IModelMapping implementations and register them.
        /// </summary>
        public void RegisterFromAssembly(Assembly assembly)
        {
            var mappingTypes = assembly.GetTypes()
                .Where(t => !t.IsAbstract && !t.IsInterface)
                .SelectMany(t => t.GetInterfaces()
                    .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IModelMapping<>))
                    .Select(i => new { MappingType = t, ModelType = i.GetGenericArguments()[0], Interface = i }));

            foreach (var entry in mappingTypes)
            {
                var instance = Activator.CreateInstance(entry.MappingType);
                if (instance == null) continue;

                var mapType = typeof(ModelMap<>).MakeGenericType(entry.ModelType);
                var map = Activator.CreateInstance(mapType);
                if (map == null) continue;

                var configureMethod = entry.Interface.GetMethod("Configure");
                configureMethod?.Invoke(instance, new[] { map });

                _maps[entry.ModelType] = map;
            }
        }

        /// <summary>
        /// Get the mapping for a model type. Returns null if not registered.
        /// </summary>
        public ModelMap<T>? GetMap<T>() where T : class
        {
            return _maps.TryGetValue(typeof(T), out var map) ? (ModelMap<T>)map : null;
        }

        /// <summary>
        /// Check if a mapping exists for a model type.
        /// </summary>
        public bool HasMap<T>() where T : class
        {
            return _maps.ContainsKey(typeof(T));
        }

        /// <summary>
        /// Check if a mapping exists for a model type.
        /// </summary>
        public bool HasMap(Type modelType)
        {
            return _maps.ContainsKey(modelType);
        }

        /// <summary>
        /// Get all registered type → table name mappings.
        /// Useful for registering with DataBase.RegisterTableNames().
        /// </summary>
        public IEnumerable<KeyValuePair<Type, string>> GetTableNames()
        {
            foreach (var (type, map) in _maps)
            {
                var tableNameProp = map.GetType().GetProperty("TableName");
                var tableName = tableNameProp?.GetValue(map) as string;
                if (!string.IsNullOrEmpty(tableName))
                    yield return new KeyValuePair<Type, string>(type, tableName);
            }
        }
    }
}
