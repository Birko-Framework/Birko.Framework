using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Birko.Data.Patterns.Schema;

namespace Birko.Models.SQL.Mapping
{
    public class ModelMapRegistry
    {
        // CR-L322: store the non-generic IModelMap view so TableName/Properties are read by a direct cast
        // rather than string-keyed reflection.
        private readonly Dictionary<Type, IModelMap> _maps = new Dictionary<Type, IModelMap>();

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
                if (map is not IModelMap modelMap) continue;

                var configureMethod = entry.Interface.GetMethod("Configure");
                configureMethod?.Invoke(instance, new[] { map });

                _maps[entry.ModelType] = modelMap;
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
                // CR-L322: direct property read via IModelMap (was GetType().GetProperty("TableName")).
                var tableName = map.TableName;
                if (!string.IsNullOrEmpty(tableName))
                    yield return new KeyValuePair<Type, string>(type, tableName);
            }
        }

        public IReadOnlyList<FieldDescriptor> GetPropertyMaps(Type modelType)
        {
            if (!_maps.TryGetValue(modelType, out var map))
                return Array.Empty<FieldDescriptor>();
            // CR-L322: direct property read via IModelMap (was GetType().GetProperty("Properties")).
            return map.Properties;
        }

        /// <summary>
        /// Patches table names and the applicable field flags onto the loaded SQL schema.
        /// Applied: table name, <c>HasColumnName</c> (→ column name), the
        /// <c>IsPrimaryKey</c>/<c>IsUnique</c>/<c>IsRequired</c>/<c>IsAutoIncrement</c> flags, and
        /// <c>HasIndex</c> / <c>HasUniqueIndex</c>, which are written into the table's <c>Indexes</c>, the same declarations
        /// the attribute path fills and schema-ensure creates (TASK-595). Measured before it: every
        /// <c>Birko.Models.Users.SQL</c> table had zero indexes after this call, though the mappings declared seven.
        /// <para>
        /// <b>Not applied</b> — <c>HasMaxLength</c>, <c>HasPrecision</c> and <c>HasScale</c> are mapping metadata only. The
        /// concrete SQL field type (length/precision/scale) is fixed at construction from the model's SQL field attributes
        /// when the table is loaded, and <see cref="Birko.Data.SQL.Fields.AbstractField"/> has no length/precision members
        /// to assign afterwards. Declare those on the model (SQL field attributes) instead; the fluent options remain
        /// readable via <see cref="GetPropertyMaps"/> for other consumers.
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

                var renamed = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var field in properties)
                {
                    var sqlField = table.GetFieldByPropertyName(field.Name);
                    if (sqlField == null) continue;

                    // HasColumnName overrides the physical column name — previously dropped silently
                    // even though AbstractField.Name is settable, so the mapping appeared to work.
                    if (!string.IsNullOrEmpty(field.ColumnName) && field.ColumnName != sqlField.Name)
                    {
                        renamed[sqlField.Name] = field.ColumnName!;
                        sqlField.Name = field.ColumnName!;
                    }

                    // TASK-567: through DeclarePrimary, so a fluent key replaces the implicit Guid key.
                    if (field.IsPrimary) table.DeclarePrimary(sqlField);
                    if (field.IsUnique) sqlField.IsUnique = true;
                    if (field.IsRequired) sqlField.IsNotNull = true;
                    if (field.IsAutoIncrement) sqlField.IsAutoincrement = true;

                    // MaxLength / Precision / Scale are intentionally not applied here — see the method doc. They
                    // cannot be assigned onto an already-loaded AbstractField.
                }

                if (renamed.Count > 0) RenameColumns(table, renamed);
                ApplyIndexes(table, properties);
            }
        }

        /// <summary>
        /// SH-M243 (TASK-154). The table was keyed, and its attribute-declared indexes resolved, by the column names
        /// at load. Renaming a field in place left <c>Fields</c> keyed by the old name (so a lookup by the mapped column
        /// missed) and every index naming the old column, so CREATE INDEX referenced a column the table did not have.
        /// </summary>
        private static void RenameColumns(Birko.Data.SQL.Tables.Table table, IReadOnlyDictionary<string, string> renamed)
        {
            table.Fields = table.Fields.Values.ToDictionary(f => f.Name);
            if (table.Indexes == null) return;
            foreach (var index in table.Indexes.Values)
            {
                foreach (var column in index.Columns)
                    if (renamed.TryGetValue(column.ColumnName, out var to)) column.ColumnName = to;
                foreach (var predicate in index.Predicates)
                    if (renamed.TryGetValue(predicate.ColumnName, out var to)) predicate.ColumnName = to;
            }
        }

        /// <summary>
        /// Writes each fluent index into <paramref name="table"/>'s index declarations, its columns ordered by
        /// <c>IndexOrder</c> and named by their (possibly renamed) physical column. Idempotent: a column already in the
        /// index is not added twice, so applying the registry again changes nothing.
        /// </summary>
        /// <exception cref="InvalidOperationException">
        /// The columns of one index disagree on uniqueness (<c>HasIndex</c> and <c>HasUniqueIndex</c> under one name), or
        /// name a property the table does not map. A declaration that cannot be honoured is refused, never dropped.
        /// </exception>
        private static void ApplyIndexes(Birko.Data.SQL.Tables.Table table, IReadOnlyList<FieldDescriptor> properties)
        {
            foreach (var group in properties.Where(p => !string.IsNullOrEmpty(p.IndexName)).GroupBy(p => p.IndexName!))
            {
                if (group.Select(p => p.IndexUnique).Distinct().Count() > 1)
                    throw new InvalidOperationException(
                        $"Index '{group.Key}' on table '{table.Name}' mixes HasIndex and HasUniqueIndex across its columns; declare every column of it the same way.");

                table.Indexes ??= new Dictionary<string, Birko.Data.SQL.Tables.IndexDefinition>();
                if (!table.Indexes.TryGetValue(group.Key, out var index))
                {
                    index = new Birko.Data.SQL.Tables.IndexDefinition { Name = group.Key };
                    table.Indexes[group.Key] = index;
                }
                index.Unique = group.First().IndexUnique;

                foreach (var property in group.OrderBy(p => p.IndexOrder))
                {
                    var sqlField = table.GetFieldByPropertyName(property.Name)
                        ?? throw new InvalidOperationException(
                            $"Index '{group.Key}' names property '{property.Name}', which table '{table.Name}' does not map.");
                    // Rule 37: an index key's column type depends on it being an index key. MySQL and SQL Server emit an
                    // unbounded string as TEXT unless IsIndexed is set, and refuse TEXT as an index key (ERROR 1170 /
                    // Msg 1919). DataBase.LoadIndexes sets it for the attribute path; a fluent index must too.
                    sqlField.IsIndexed = true;
                    if (index.Columns.Any(c => c.ColumnName == sqlField.Name))
                        continue;
                    index.Columns.Add(new Birko.Data.SQL.Tables.IndexColumn
                    {
                        ColumnName = sqlField.Name,
                        Order = property.IndexOrder,
                        IsDescending = property.IndexDescending,
                    });
                }
            }
        }
    }
}
