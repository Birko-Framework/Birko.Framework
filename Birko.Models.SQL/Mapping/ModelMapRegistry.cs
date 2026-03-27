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

        /// <summary>
        /// Get all registered property mappings for a model type.
        /// Returns empty if no mapping is registered.
        /// </summary>
        public IReadOnlyList<PropertyMap> GetPropertyMaps(Type modelType)
        {
            if (!_maps.TryGetValue(modelType, out var map))
                return Array.Empty<PropertyMap>();
            var propsProp = map.GetType().GetProperty("Properties");
            return propsProp?.GetValue(map) as IReadOnlyList<PropertyMap> ?? Array.Empty<PropertyMap>();
        }

        /// <summary>
        /// Apply all registered mappings to the Birko SQL DataBase layer.
        /// Registers table names and applies field metadata (primary, unique, precision, etc.)
        /// from fluent PropertyMap definitions to the SQL field cache.
        /// </summary>
        public void ApplyToDatabase()
        {
            // 1. Register table names
            Birko.Data.SQL.DataBase.RegisterTableNames(GetTableNames());

            // 2. Apply property-level metadata to the field cache
            foreach (var (type, _) in _maps)
            {
                var properties = GetPropertyMaps(type);
                if (properties.Count == 0) continue;

                // Force LoadTable to populate the field cache for this type
                var table = Birko.Data.SQL.DataBase.LoadTable(type);
                if (table?.Fields == null) continue;

                foreach (var propMap in properties)
                {
                    var field = table.GetFieldByPropertyName(propMap.PropertyName);
                    if (field == null) continue;

                    if (propMap.IsPrimary) field.IsPrimary = true;
                    if (propMap.IsUnique) field.IsUnique = true;
                    if (propMap.IsRequired) field.IsNotNull = true;
                    if (propMap.IsIncrement) field.IsAutoincrement = true;
                }
            }
        }
    }
}
