using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Birko.Data.Patterns.Schema;

namespace Birko.Models.SQL.Mapping
{
    public class ModelMapRegistry
    {
        private readonly Dictionary<Type, object> _maps = new Dictionary<Type, object>();

        public void Register<T>(IModelMapping<T> mapping) where T : class
        {
            var map = new ModelMap<T>();
            mapping.Configure(map);
            _maps[typeof(T)] = map;
        }

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

        public ModelMap<T>? GetMap<T>() where T : class
        {
            return _maps.TryGetValue(typeof(T), out var map) ? (ModelMap<T>)map : null;
        }

        public bool HasMap<T>() where T : class
        {
            return _maps.ContainsKey(typeof(T));
        }

        public bool HasMap(Type modelType)
        {
            return _maps.ContainsKey(modelType);
        }

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

        public IReadOnlyList<FieldDescriptor> GetPropertyMaps(Type modelType)
        {
            if (!_maps.TryGetValue(modelType, out var map))
                return Array.Empty<FieldDescriptor>();
            var propsProp = map.GetType().GetProperty("Properties");
            return propsProp?.GetValue(map) as IReadOnlyList<FieldDescriptor> ?? Array.Empty<FieldDescriptor>();
        }

        public void ApplyToDatabase()
        {
            Birko.Data.SQL.DataBase.RegisterTableNames(GetTableNames());

            foreach (var (type, _) in _maps)
            {
                var properties = GetPropertyMaps(type);
                if (properties.Count == 0) continue;

                var table = Birko.Data.SQL.DataBase.LoadTable(type);
                if (table?.Fields == null) continue;

                foreach (var field in properties)
                {
                    var sqlField = table.GetFieldByPropertyName(field.Name);
                    if (sqlField == null) continue;

                    if (field.IsPrimary) sqlField.IsPrimary = true;
                    if (field.IsUnique) sqlField.IsUnique = true;
                    if (field.IsRequired) sqlField.IsNotNull = true;
                    if (field.IsAutoIncrement) sqlField.IsAutoincrement = true;
                }
            }
        }
    }
}
