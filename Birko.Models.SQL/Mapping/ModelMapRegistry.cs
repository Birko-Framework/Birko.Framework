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

        /// <summary>
        /// Registers every non-abstract <see cref="IModelMapping{T}"/> implementation in <paramref name="assembly"/>.
        /// </summary>
        /// <remarks>
        /// <b>Open generic mappings are skipped</b> (TASK-766): a type such as <c>EntityTranslationMapping&lt;TTranslation&gt;</c>
        /// has no model type until it is closed, and cannot be instantiated. Register it closed, through
        /// <see cref="Register{T}(IModelMapping{T})"/> or the owning project's helper (for the translation table,
        /// <c>LocalizationSqlMappings.Register&lt;TTranslation&gt;(registry, table)</c>).
        /// </remarks>
        public void RegisterFromAssembly(Assembly assembly)
        {
            var mappingTypes = assembly.GetTypes()
                .Where(t => !t.IsAbstract && !t.IsInterface && !t.ContainsGenericParameters)
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
        /// <c>HasMaxLength</c> is applied too (TASK-691), through <c>DataBase.RegisterMaxLength</c> before the table loads:
        /// a <c>string</c> becomes the bounded column a <c>[MaxLengthField]</c> gives, winning over one, and a <c>byte[]</c>
        /// is bounded likewise. On any other type it is refused (rule 55). A length above a provider's index-key cap is
        /// rendered as declared, and the database refuses the index, which schema-ensure reports, as with the attribute.
        /// </para>
        /// <para>
        /// <c>HasPrecision</c> / <c>HasScale</c> are applied to a <c>decimal</c> column (TASK-692), through
        /// <c>DataBase.RegisterPrecision</c>, winning over <c>[PrecisionField]</c> / <c>[ScaleField]</c>. On any other type
        /// they are refused (rule 55); a string's length is <c>HasMaxLength</c>. Before, both were accepted and dropped: a
        /// decimal got the provider default, and a string's <c>HasPrecision(n)</c> left it unbounded.
        /// </para>
        /// </summary>
        public void ApplyToDatabase()
        {
            Birko.Data.SQL.DataBase.RegisterTableNames(GetTableNames());
            foreach (var (type, map) in _maps)
            {
                // TASK-272: a schema declared with ToTable(name, schema).
                if (!string.IsNullOrEmpty(map.TableName) && map.TableSchema != null)
                    Birko.Data.SQL.DataBase.RegisterTableName(type, map.TableName!, map.TableSchema);
            }

            // TASK-691: lengths first. A changed one rebuilds the type's fields, so it must come before the flags below are
            // written onto them.
            foreach (var (type, _) in _maps)
            {
                RegisterMaxLengths(type, GetPropertyMaps(type));
                RegisterPrecisions(type, GetPropertyMaps(type));
            }

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

                    // MaxLength, Precision and Scale were registered before the load (TASK-691, TASK-692).
                }

                if (renamed.Count > 0) RenameColumns(table, renamed);
                ApplyIndexes(table, properties);
            }
        }

        /// <summary>
        /// Registers each <c>HasMaxLength</c> of <paramref name="type"/>'s mapping (TASK-691).
        /// </summary>
        /// <exception cref="InvalidOperationException">
        /// A <c>HasMaxLength</c> that cannot be honoured, refused rather than dropped (rule 55): a length that is not
        /// positive; a property that is neither <c>string</c> nor <c>byte[]</c> (no column of that type has a length); or a
        /// property that is no column at all (ignored, or without a public instance setter).
        /// </exception>
        private static void RegisterMaxLengths(Type type, IReadOnlyList<FieldDescriptor> properties)
        {
            foreach (var field in properties)
            {
                if (field.MaxLength is not int maxLength)
                {
                    continue;
                }
                var where = $"HasMaxLength({maxLength}) on {type.Name}.{field.Name}";
                if (maxLength <= 0)
                {
                    throw new InvalidOperationException($"{where}: a length must be positive.");
                }
                var property = DeclaredProperty(type, field.Name)
                    ?? throw new InvalidOperationException($"{where}: {type.Name} has no such public property.");
                if (property.PropertyType != typeof(string) && property.PropertyType != typeof(byte[]))
                {
                    throw new InvalidOperationException(
                        $"{where}, a {property.PropertyType.Name}: only a string or byte[] column has a length.");
                }
                if (field.IsIgnored || !property.CanWrite
                    || property.IsDefined(typeof(Birko.Data.SQL.Attributes.IgnoreField), true))
                {
                    throw new InvalidOperationException($"{where}: the property is not a column (ignored, or it has no setter).");
                }
                Birko.Data.SQL.DataBase.RegisterMaxLength(type, field.Name, maxLength);
            }
        }

        /// <summary>
        /// Registers each <c>HasPrecision</c> / <c>HasScale</c> of <paramref name="type"/>'s mapping (TASK-692).
        /// </summary>
        /// <exception cref="InvalidOperationException">
        /// One that cannot be honoured, refused rather than dropped (rule 55): a property that is not <c>decimal</c> (a
        /// string's length is <c>HasMaxLength</c>); a precision that is not positive; a negative scale, or one above the
        /// precision; a property that is no column.
        /// </exception>
        private static void RegisterPrecisions(Type type, IReadOnlyList<FieldDescriptor> properties)
        {
            foreach (var field in properties)
            {
                if (field.Precision == null && field.Scale == null)
                {
                    continue;
                }
                var where = $"{(field.Precision != null ? $"HasPrecision({field.Precision})" : "")}"
                    + $"{(field.Precision != null && field.Scale != null ? "." : "")}"
                    + $"{(field.Scale != null ? $"HasScale({field.Scale})" : "")} on {type.Name}.{field.Name}";
                var property = DeclaredProperty(type, field.Name)
                    ?? throw new InvalidOperationException($"{where}: {type.Name} has no such public property.");
                var clr = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
                if (clr == typeof(string))
                {
                    throw new InvalidOperationException(
                        $"{where}, a string: precision and scale are a decimal's. A string's length is HasMaxLength(n).");
                }
                if (clr != typeof(decimal))
                {
                    throw new InvalidOperationException(
                        $"{where}, a {clr.Name}: only a decimal column has a precision and scale.");
                }
                if (field.Precision is int precision && precision <= 0)
                {
                    throw new InvalidOperationException($"{where}: a precision must be positive.");
                }
                if (field.Scale is int scale && (scale < 0 || (field.Precision is int p && scale > p)))
                {
                    throw new InvalidOperationException($"{where}: a scale must be between 0 and the precision.");
                }
                if (field.IsIgnored || !property.CanWrite
                    || property.IsDefined(typeof(Birko.Data.SQL.Attributes.IgnoreField), true))
                {
                    throw new InvalidOperationException($"{where}: the property is not a column (ignored, or it has no setter).");
                }
                Birko.Data.SQL.DataBase.RegisterPrecision(type, field.Name, field.Precision, field.Scale);
            }
        }

        /// <summary>The most derived public instance property of that name: a property hidden with <c>new</c> is not ambiguous.</summary>
        private static PropertyInfo? DeclaredProperty(Type type, string name)
        {
            for (var current = type; current != null; current = current.BaseType)
            {
                var property = current.GetProperty(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
                if (property != null)
                {
                    return property;
                }
            }
            return null;
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
            var memberships = properties.SelectMany(p => p.Indexes.Select(index => (Property: p, Index: index)));
            foreach (var group in memberships.GroupBy(m => m.Index.Name))
            {
                if (group.Select(m => m.Index.Unique).Distinct().Count() > 1)
                    throw new InvalidOperationException(
                        $"Index '{group.Key}' on table '{table.Name}' mixes HasIndex and HasUniqueIndex across its columns; declare every column of it the same way.");

                table.Indexes ??= new Dictionary<string, Birko.Data.SQL.Tables.IndexDefinition>();
                if (!table.Indexes.TryGetValue(group.Key, out var index))
                {
                    index = new Birko.Data.SQL.Tables.IndexDefinition { Name = group.Key };
                    table.Indexes[group.Key] = index;
                }
                index.Unique = group.First().Index.Unique;

                foreach (var (property, membership) in group.OrderBy(m => m.Index.Order))
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
                        Order = membership.Order,
                        IsDescending = membership.Descending,
                    });
                }
            }
        }
    }
}
