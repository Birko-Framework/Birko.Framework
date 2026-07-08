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

        /// <summary>
        /// Patches table names and the applicable field flags onto the loaded SQL schema.
        /// Applied: table name, <c>HasColumnName</c> (→ column name), and the
        /// <c>IsPrimaryKey</c>/<c>IsUnique</c>/<c>IsRequired</c>/<c>IsAutoIncrement</c> flags.
        /// <para>
        /// <b>Not applied</b> — <c>HasMaxLength</c>, <c>HasPrecision</c>, <c>HasScale</c> and
        /// <c>HasIndex</c> are mapping metadata only. The concrete SQL field type (length/precision/
        /// scale) is fixed at construction from the model's SQL field attributes when the table is
        /// loaded, and <see cref="Birko.Data.SQL.Fields.AbstractField"/> has no length/precision/index
        /// members to assign afterwards. Declare those on the model (SQL field attributes) instead;
        /// the fluent options remain readable via <see cref="GetPropertyMaps"/> for other consumers.
        /// </para>
        /// </summary>
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

                    // HasColumnName overrides the physical column name — previously dropped silently
                    // even though AbstractField.Name is settable, so the mapping appeared to work.
                    if (!string.IsNullOrEmpty(field.ColumnName)) sqlField.Name = field.ColumnName;

                    if (field.IsPrimary) sqlField.IsPrimary = true;
                    if (field.IsUnique) sqlField.IsUnique = true;
                    if (field.IsRequired) sqlField.IsNotNull = true;
                    if (field.IsAutoIncrement) sqlField.IsAutoincrement = true;

                    // MaxLength / Precision / Scale / index are intentionally not applied here — see
                    // the method doc. They cannot be assigned onto an already-loaded AbstractField.
                }
            }
        }
    }
}
