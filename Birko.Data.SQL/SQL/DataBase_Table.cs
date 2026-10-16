using Birko.Data.SQL.Fields;
using Birko.Data.SQL.Tables;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;

namespace Birko.Data.SQL
{
    public static partial class DataBase
    {
        private static readonly ConcurrentDictionary<Type, Tables.Table> _tableCache = new();
        private static readonly ConcurrentDictionary<Type, string> _tableNameOverrides = new();

        /// <summary>
        /// Register a table name for a model type (used by fluent mapping systems).
        /// This takes precedence over attribute-based discovery when no [Table] attribute is present.
        /// </summary>
        private static readonly ConcurrentDictionary<Type, string> _tableSchemaOverrides = new();

        public static void RegisterTableName(Type modelType, string tableName) => RegisterTableName(modelType, tableName, null);

        /// <summary>
        /// Maps <paramref name="modelType"/> to <paramref name="tableName"/> in <paramref name="schema"/> (null: the
        /// connection's default), as <c>ModelMap.ToTable(name, schema)</c> does (TASK-272).
        /// </summary>
        public static void RegisterTableName(Type modelType, string tableName, string? schema)
        {
            if (schema == null)
                _tableSchemaOverrides.TryRemove(modelType, out _);
            else
                _tableSchemaOverrides[modelType] = ValidateSchemaName(schema, modelType);
            ForgetTableSchema(modelType);
            _tableNameOverrides[modelType] = tableName;
            // Invalidate cache so LoadTable picks up the override - including a cached "maps to no table" (TASK-154),
            // which would otherwise outlive the registration that gives the type its table.
            _tableCache.TryRemove(modelType, out _);
            _unmappedTypes.TryRemove(modelType, out _);
        }

        /// <summary>
        /// Register table names for all model mappings in the given registry.
        /// </summary>
        public static void RegisterTableNames(IEnumerable<KeyValuePair<Type, string>> mappings)
        {
            foreach (var (type, name) in mappings)
                RegisterTableName(type, name);
        }

        public static IEnumerable<Tables.Table> LoadTables(IEnumerable<Type> types)
        {
            if (types != null && types.Any())
            {
                List<Tables.Table> tables = new List<Tables.Table>();
                foreach (Type type in types)
                {
                    var table = TryLoadTable(type);
                    if (table != null && table.Fields != null && table.Fields.Any())
                    {
                        tables.Add(table);
                    }
                }
                return tables.ToArray();
            }
            else
            {
                throw new Exceptions.TableAttributeException("Types enumerable is empty or null");
            }
        }

        /// <summary>
        /// The table <paramref name="type"/> maps to. Throws <see cref="Exceptions.TableAttributeException"/> for a type
        /// that maps to none (no <c>[Table]</c>, <c>[System.ComponentModel.DataAnnotations.Schema.Table]</c> or
        /// registered name, or no columns).
        /// </summary>
        /// <remarks>
        /// SH-M222 (TASK-154). This returned null through a non-nullable type (<c>return table!</c>), so a model missing
        /// <c>[Table]</c> failed with an opaque NullReferenceException, or not at all: measured, a <c>Select</c> on such
        /// a type answered 0 rows, and MySQL's bulk insert returned having inserted nothing. A caller to whom "not a
        /// table" is a legitimate answer uses <see cref="TryLoadTable"/>.
        /// </remarks>
        public static Tables.Table LoadTable(Type type)
            => TryLoadTable(type) ?? throw new Exceptions.TableAttributeException(
                $"{type.FullName} maps to no table: give it a [Table(\"name\")] attribute (or register a table name) and at "
                + "least one mapped property.");

        /// <summary>The table <paramref name="type"/> maps to, or null when it maps to none. Both answers are cached.</summary>
        public static Tables.Table? TryLoadTable(Type type)
        {
            if (_tableCache.TryGetValue(type, out var cached))
                return cached;
            if (_unmappedTypes.ContainsKey(type))
                return null;

            var table = ComputeTable(type);
            if (table != null)
                _tableCache.TryAdd(type, table);
            else
                _unmappedTypes.TryAdd(type, 0);
            return table;
        }

        private static readonly ConcurrentDictionary<Type, byte> _unmappedTypes = new();

        private static Tables.Table? ComputeTable(Type type)
        {
            // Check both Birko and DataAnnotations table attributes.
            // Use name-based matching for Birko.Data.SQL.Attributes.Table to handle
            // shared-project type identity issues (same attribute compiled into multiple assemblies).
            IEnumerable<object> attrs = type.GetCustomAttributes(true)
                .Where(a => a is Birko.Data.SQL.Attributes.Table
                         || a is System.ComponentModel.DataAnnotations.Schema.TableAttribute
                         || a.GetType().FullName == "Birko.Data.SQL.Attributes.Table");
            if (attrs != null)
            {
                foreach (Attribute attr in attrs)
                {
                    string? tableName = null;
                    string? schema = null;
                    if (attr is Birko.Data.SQL.Attributes.Table birkoTable)
                    {
                        tableName = birkoTable.Name;
                        schema = birkoTable.Schema;
                    }
                    else if (attr is System.ComponentModel.DataAnnotations.Schema.TableAttribute dataTable)
                    {
                        // TASK-272: its Schema was read by nobody, so [Table("t", Schema = "s")] landed in the default schema.
                        tableName = dataTable.Name;
                        schema = dataTable.Schema;
                    }
                    else if (attr.GetType().FullName == "Birko.Data.SQL.Attributes.Table")
                    {
                        // Cross-assembly shared-project attribute — read Name and Schema via reflection
                        tableName = attr.GetType().GetProperty("Name")?.GetValue(attr) as string;
                        schema = attr.GetType().GetProperty("Schema")?.GetValue(attr) as string;
                    }
                    if (!string.IsNullOrEmpty(tableName))
                    {
                        Tables.Table table = new Tables.Table()
                        {
                            Name = tableName,
                            Schema = string.IsNullOrEmpty(schema) ? null : ValidateSchemaName(schema, type),
                            Type = type,
                            Fields = LoadFields(type).ToDictionary(x => x.Name),
                        };
                        if (table.Fields != null && table.Fields.Any())
                        {
                            foreach (var field in table.Fields)
                            {
                                field.Value.Table = table;
                            }
                            table.PromoteGuidToKey();
                            table.Indexes = LoadIndexes(type, table.Fields, table.Name);
                            RecordTableSchema(table);
                            return table;
                        }
                    }
                }
            }

            // Fallback: check fluent mapping overrides (registered via RegisterTableName)
            if (_tableNameOverrides.TryGetValue(type, out var overrideName))
            {
                var table = new Tables.Table()
                {
                    Name = overrideName,
                    Schema = _tableSchemaOverrides.TryGetValue(type, out var overrideSchema) ? overrideSchema : null,
                    Type = type,
                    Fields = LoadFields(type).ToDictionary(x => x.Name),
                };
                if (table.Fields != null && table.Fields.Any())
                {
                    foreach (var field in table.Fields)
                        field.Value.Table = table;
                    table.PromoteGuidToKey();
                    table.Indexes = LoadIndexes(type, table.Fields, table.Name);
                    RecordTableSchema(table);
                    return table;
                }
            }

            return null;
        }

        /// <summary>Logical table name → (schema, the type that declared it). Case-insensitive, as identifiers are.</summary>
        private static readonly ConcurrentDictionary<string, (string? Schema, Type Type)> _tableSchemas
            = new(StringComparer.OrdinalIgnoreCase);
        private static readonly object _tableSchemasLock = new();

        /// <summary>
        /// The schema a loaded entity declared for <paramref name="tableName"/> (a logical table name), or null when it
        /// declared none or no loaded entity has that name (TASK-272).
        /// </summary>
        /// <remarks>
        /// A sink that knows only the name (a FROM list, a join, an index or a catalogue lookup) asks here. An entity's
        /// table is loaded before any statement against it, so its schema is known by then.
        /// </remarks>
        public static string? TableSchema(string tableName)
            => !string.IsNullOrEmpty(tableName) && _tableSchemas.TryGetValue(tableName, out var entry) ? entry.Schema : null;

        /// <summary>The prefixed table name a schema-less provider stores <paramref name="schema"/>.<paramref name="name"/> as.</summary>
        public static string PrefixedTableName(string schema, string name) => schema + "__" + name;

        private static void ForgetTableSchema(Type type)
        {
            lock (_tableSchemasLock)
            {
                foreach (var entry in _tableSchemas.Where(e => e.Value.Type == type).ToList())
                    _tableSchemas.TryRemove(entry.Key, out _);
            }
        }

        /// <summary>
        /// Registers a loaded table's schema, refusing a declaration two providers would resolve to one relation.
        /// </summary>
        /// <remarks>
        /// TASK-272, the user's decision: on MySQL and SQLite <c>reporting.evts</c> is the table <c>reporting__evts</c>, so
        /// that and a table declared as <c>reporting__evts</c> would be one table there. The same logical name in two
        /// schemas is refused too: the logical name is the alias every column qualifier names. Checked for every
        /// provider at once, so a model that loads works on all of them.
        /// </remarks>
        private static void RecordTableSchema(Tables.Table table)
        {
            lock (_tableSchemasLock)
            {
                if (_tableSchemas.TryGetValue(table.Name, out var existing) && existing.Type != table.Type
                    && !string.Equals(existing.Schema, table.Schema, StringComparison.OrdinalIgnoreCase))
                {
                    throw new Exceptions.TableAttributeException(
                        $"Table '{table.Name}' is declared in schema '{existing.Schema ?? "(default)"}' by {existing.Type.Name} "
                        + $"and in '{table.Schema ?? "(default)"}' by {table.Type.Name}. A table name is the alias its columns are "
                        + "qualified with, so it can live in one schema only.");
                }

                var physical = table.Schema == null ? table.Name : PrefixedTableName(table.Schema, table.Name);
                foreach (var other in _tableSchemas)
                {
                    if (other.Value.Type == table.Type) continue;
                    var otherPhysical = other.Value.Schema == null ? other.Key : PrefixedTableName(other.Value.Schema, other.Key);
                    if (string.Equals(otherPhysical, physical, StringComparison.OrdinalIgnoreCase)
                        && !string.Equals(other.Key, table.Name, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new Exceptions.TableAttributeException(
                            $"{table.Type.Name} ({table.Schema}.{table.Name}) and {other.Value.Type.Name} ({other.Value.Schema ?? "(default)"}."
                            + $"{other.Key}) are both the table '{physical}' on MySQL and SQLite, which store a schema as a "
                            + "'schema__' prefix. Rename one of them.");
                    }
                }
                _tableSchemas[table.Name] = (table.Schema, table.Type);
            }
        }

        /// <summary>
        /// A schema is a plain identifier: it reaches DDL bare inside a quoted part, and on MySQL and SQLite it becomes part of
        /// a table name, so nothing else is accepted (rule 33: a name with no enclosure is contained by refusal).
        /// </summary>
        internal static string ValidateSchemaName(string schema, Type? type = null)
        {
            if (string.IsNullOrEmpty(schema) || !(char.IsLetter(schema[0]) || schema[0] == '_')
                || !schema.All(c => (c < 128 && char.IsLetterOrDigit(c)) || c == '_'))
            {
                throw new Exceptions.TableAttributeException(
                    $"Schema '{schema}'{(type == null ? "" : $" of {type.Name}")} is not a plain identifier (letters, digits, underscores).");
            }
            return schema;
        }

        /// <param name="tableName">
        /// The table the indexes belong to, used to name the unique indexes synthesised for nullable
        /// <c>[UniqueField]</c> columns (TASK-275). Optional so existing callers compile; when it is null no
        /// such index is synthesised, which is why <c>LoadTable</c> passes it.
        /// </param>
        public static Dictionary<string, IndexDefinition> LoadIndexes(Type type, Dictionary<string, AbstractField> fields, string? tableName = null)
        {
            var indexes = new Dictionary<string, IndexDefinition>();

            // TASK-273 — index name -> (NOT NULL names, NULL names), accumulated across both attribute forms
            // and resolved once at the end. Kept as declared names rather than resolved columns so the
            // contradiction check compares what the author wrote.
            var predicateNames = new Dictionary<string, (List<string> NotNull, List<string> Null)>();

            foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                // Direct cast
                var directAttrs = prop.GetCustomAttributes(typeof(Attributes.IndexedField), true)
                    .OfType<Attributes.IndexedField>();

                // Cross-assembly shared-project fallback
                var crossAttrs = prop.GetCustomAttributes(true)
                    .Where(a => a.GetType().FullName == "Birko.Data.SQL.Attributes.IndexedField"
                             && !(a is Attributes.IndexedField));

                var allAttrs = new List<(string Name, int Order, bool IsDescending, bool IsUnique, string[] WhereNotNull, string[] WhereNull)>();

                foreach (var attr in directAttrs)
                {
                    allAttrs.Add((attr.Name, attr.Order, attr.IsDescending, attr.IsUnique, attr.WhereNotNull, attr.WhereNull));
                }

                foreach (var attr in crossAttrs)
                {
                    var attrType = attr.GetType();
                    var name = attrType.GetProperty("Name")?.GetValue(attr) as string;
                    var order = attrType.GetProperty("Order")?.GetValue(attr) is int o ? o : 0;
                    var isDesc = attrType.GetProperty("IsDescending")?.GetValue(attr) is bool d && d;
                    var isUnique = attrType.GetProperty("IsUnique")?.GetValue(attr) is bool u && u;
                    // TASK-273 — the reflective path must read the predicate lists too. A new property read
                    // only through the direct cast above works in this framework's own tests and silently
                    // does nothing for the shared-project consumers the feature exists for.
                    var whereNotNull = attrType.GetProperty("WhereNotNull")?.GetValue(attr) as string[];
                    var whereNull = attrType.GetProperty("WhereNull")?.GetValue(attr) as string[];
                    if (!string.IsNullOrEmpty(name))
                    {
                        allAttrs.Add((name!, order, isDesc, isUnique,
                            whereNotNull ?? Array.Empty<string>(), whereNull ?? Array.Empty<string>()));
                    }
                }

                // SH-M242 (TASK-154): [IndexedField] is inherited, so one on a base class's property named the same index
                // in every derived table. Index names are database-wide on PostgreSQL and SQLite, so CREATE INDEX IF
                // NOT EXISTS created it for the first table and silently skipped the rest (measured on SQLite).
                // [CompositeIndex] is declared per class and not inherited, which is what this case needs.
                if (allAttrs.Count > 0 && prop.DeclaringType != type)
                {
                    throw new Exceptions.TableAttributeException(
                        $"{type.FullName}: [IndexedField(\"{allAttrs[0].Name}\")] is declared on {prop.DeclaringType?.Name}.{prop.Name}, "
                        + "a base class, so every derived table would declare the same index name and all but the first "
                        + "would be skipped. Declare a [CompositeIndex] with a table-specific name on each derived class.");
                }

                foreach (var (name, order, isDescending, isUnique, whereNotNullNames, whereNullNames) in allAttrs)
                {
                    if (!indexes.TryGetValue(name, out var idx))
                    {
                        idx = new IndexDefinition { Name = name };
                        indexes[name] = idx;
                    }

                    // Any contributing [IndexedField] marking the index unique makes the whole index unique.
                    if (isUnique)
                    {
                        idx.Unique = true;
                    }

                    // Resolve actual column name from field metadata
                    var field = fields.Values.FirstOrDefault(f => f.Property?.Name == prop.Name);
                    // SH-M223 (TASK-154): an unmapped property ([IgnoreField], no setter) used to name the index column
                    // anyway, and CREATE INDEX failed far from the declaration. [CompositeIndex] already refused this.
                    if (field == null)
                    {
                        throw new Exceptions.TableAttributeException(
                            $"[IndexedField(\"{name}\")] on {type.FullName}.{prop.Name}: the property is not a mapped column.");
                    }
                    var columnName = field.Name;

                    // TASK-248: a provider whose column type depends on being indexed needs to know here,
                    // because this is the only place the declaration is resolved against the field. MySQL
                    // maps an unbounded string to LONGTEXT and cannot index BLOB/TEXT without a key length.
                    if (field != null)
                    {
                        field.IsIndexed = true;
                    }

                    idx.Columns.Add(new IndexColumn
                    {
                        ColumnName = columnName,
                        Order = order,
                        IsDescending = isDescending,
                    });

                    // TASK-273 — merged across every attribute contributing to this index name, exactly as
                    // IsUnique is, and validated once after the merge (see ApplyIndexPredicates). Two
                    // properties can each name the same column in the OPPOSITE list, which is a contradiction
                    // that indexes no rows and is invisible until the lists are combined.
                    AddPredicateNames(predicateNames, name, whereNotNullNames, whereNullNames);
                }
            }

            // Class-level [CompositeIndex] — declares an index whose columns may be inherited from a base
            // class (e.g. (TenantGuid, Number) where TenantGuid lives on a shared base type). Property names
            // resolve against the same `fields` map (which already includes inherited properties), so
            // [NamedField]/ModelMap column remaps are honoured automatically.
            var compositeAttrs = new List<(string Name, string[] Properties, bool IsUnique, string[] WhereNotNull, string[] WhereNull)>();

            // Direct cast (Inherited = false — do not walk base classes)
            foreach (var attr in type.GetCustomAttributes(typeof(Attributes.CompositeIndex), false)
                .OfType<Attributes.CompositeIndex>())
            {
                compositeAttrs.Add((attr.Name, attr.Properties, attr.IsUnique, attr.WhereNotNull, attr.WhereNull));
            }

            // Cross-assembly shared-project fallback (same attribute compiled into another assembly)
            foreach (var attr in type.GetCustomAttributes(false)
                .Where(a => a.GetType().FullName == "Birko.Data.SQL.Attributes.CompositeIndex"
                         && !(a is Attributes.CompositeIndex)))
            {
                var attrType = attr.GetType();
                var name = attrType.GetProperty("Name")?.GetValue(attr) as string;
                var props = attrType.GetProperty("Properties")?.GetValue(attr) as string[];
                var isUnique = attrType.GetProperty("IsUnique")?.GetValue(attr) is bool cu && cu;
                // TASK-273 — same reflective read as the per-property path above, for the same reason.
                var whereNotNull = attrType.GetProperty("WhereNotNull")?.GetValue(attr) as string[];
                var whereNull = attrType.GetProperty("WhereNull")?.GetValue(attr) as string[];
                if (!string.IsNullOrEmpty(name) && props != null)
                {
                    compositeAttrs.Add((name!, props, isUnique,
                        whereNotNull ?? Array.Empty<string>(), whereNull ?? Array.Empty<string>()));
                }
            }

            foreach (var (name, properties, isUnique, whereNotNullNames, whereNullNames) in compositeAttrs)
            {
                if (!indexes.TryGetValue(name, out var idx))
                {
                    idx = new IndexDefinition { Name = name };
                    indexes[name] = idx;
                }
                if (isUnique)
                {
                    idx.Unique = true;
                }

                for (int i = 0; i < properties.Length; i++)
                {
                    var propName = properties[i];
                    var field = fields.Values.FirstOrDefault(f => f.Property?.Name == propName);
                    var columnName = field?.Name;
                    if (string.IsNullOrEmpty(columnName))
                    {
                        // A typo must break at table-load, never silently drop the constraint.
                        throw new Exceptions.TableAttributeException(
                            $"CompositeIndex '{name}' on {type.FullName}: property '{propName}' is not a mapped column");
                    }

                    // TASK-248 — same marking as the per-property path above. Both resolution points must
                    // set it or a composite-only index would leave its columns looking unindexed.
                    field!.IsIndexed = true;

                    idx.Columns.Add(new IndexColumn
                    {
                        ColumnName = columnName,
                        Order = i,
                        IsDescending = false,
                    });
                }

                // TASK-273 — outside the column loop: a predicate column need not be a key column, so it is
                // per-declaration, not per-key-column.
                AddPredicateNames(predicateNames, name, whereNotNullNames, whereNullNames);
            }

            // TASK-275 — a nullable [UniqueField] column carries its constraint as a partial unique index
            // rather than as an inline UNIQUE. Synthesised BEFORE the predicates are resolved so it goes
            // through exactly the same rendering, refusal and per-provider policy as a declared one.
            SynthesiseNullableUniqueIndexes(type, indexes, predicateNames, fields, tableName);

            // TASK-273 — resolve the merged predicate names once both attribute forms have contributed.
            ApplyIndexPredicates(type, indexes, predicateNames, fields);

            // Sort columns by Order within each index
            // SH-M224 (TASK-154): List.Sort is unstable, so columns with the same Order (the default 0) came out in
            // no defined order, and column order decides which queries an index serves. Stable: Order, then the
            // order the columns were declared in.
            foreach (var idx in indexes.Values)
            {
                var ordered = idx.Columns.Select((column, position) => (column, position))
                    .OrderBy(c => c.column.Order).ThenBy(c => c.position).Select(c => c.column).ToList();
                idx.Columns.Clear();
                idx.Columns.AddRange(ordered);
            }

            return indexes;
        }

        /// <summary>
        /// Gives every nullable <c>[UniqueField]</c> column a unique index filtered to its non-null rows,
        /// replacing the inline <c>UNIQUE</c> that <c>FieldDefinition</c> no longer emits for it (TASK-275).
        /// </summary>
        /// <remarks>
        /// <para>
        /// The inline form is broken on SQL Server and only there — it treats NULLs as equal, so it admits
        /// one NULL row and rejects every later row that leaves the column unset (<c>Msg 2627</c>) — and a
        /// predicate cannot be attached to it (<c>Msg 156</c>). Expressing it as
        /// <c>CREATE UNIQUE INDEX … WHERE col IS NOT NULL</c> reuses TASK-273's machinery whole, including
        /// the MySQL policy that drops the term because NULLs are already distinct there.
        /// </para>
        /// <para>
        /// <b>Named <c>ux_{table}_{column}</c>, and a collision throws.</b> Index names are database-global on
        /// PostgreSQL and SQLite, so the table name has to be in it. If a declared index already owns that
        /// name this refuses rather than merging into it: silently adding a column to somebody else's index
        /// would change their constraint (§ SH-H037, and the same reasoning as the view-field key collision
        /// in TASK-207).
        /// </para>
        /// <para>
        /// <c>IsIndexed</c> is set for the same reason the two declared paths set it (TASK-248): the column is
        /// now an index key, so a provider whose key types are restricted must know. On MySQL that is what
        /// makes the synthesised index buildable at all — an unlengthed string is <c>LONGTEXT</c> otherwise
        /// and cannot be indexed (ERROR 1170). It does not change <see cref="AbstractField.IsInIndexKey"/>,
        /// which was already true via <c>IsUnique</c>.
        /// </para>
        /// </remarks>
        private static void SynthesiseNullableUniqueIndexes(
            Type type,
            Dictionary<string, IndexDefinition> indexes,
            Dictionary<string, (List<string> NotNull, List<string> Null)> predicateNames,
            Dictionary<string, AbstractField> fields,
            string? tableName)
        {
            if (string.IsNullOrEmpty(tableName))
            {
                return;
            }

            foreach (var field in fields.Values)
            {
                if (field == null || field.UsesInlineUniqueConstraint || !field.IsUnique || field.IsPrimary)
                {
                    continue;
                }

                var name = $"ux_{tableName}_{field.Name}";
                if (indexes.TryGetValue(name, out var existing))
                {
                    var alreadyOurs = existing.Unique
                        && existing.Columns.Count == 1
                        && existing.Columns[0].ColumnName == field.Name;
                    if (alreadyOurs)
                    {
                        continue;
                    }
                    throw new Exceptions.TableAttributeException(
                        $"{type.FullName}: the unique index synthesised for nullable [UniqueField] column '{field.Name}' "
                        + $"would be named '{name}', which a declared index already uses. Rename the declared index, or "
                        + "mark the column [RequiredField] so its UNIQUE stays inline.");
                }

                var idx = new IndexDefinition { Name = name, Unique = true };
                idx.Columns.Add(new IndexColumn { ColumnName = field.Name, Order = 0 });
                indexes[name] = idx;

                field.IsIndexed = true;

                // Routed through the same accumulator a declared WhereNotNull uses, so the term is resolved,
                // validated and rendered by one path rather than two.
                AddPredicateNames(predicateNames, name, new[] { field.Property?.Name ?? field.Name }, Array.Empty<string>());
            }
        }

        /// <summary>
        /// Accumulates one declaration's predicate names under its index name (TASK-273). De-duplicating
        /// here rather than at the end keeps a repeated name from producing <c>X IS NOT NULL AND X IS NOT
        /// NULL</c>, which is valid SQL and would still break the byte-identical DDL assertions.
        /// </summary>
        private static void AddPredicateNames(
            Dictionary<string, (List<string> NotNull, List<string> Null)> acc,
            string indexName, string[] whereNotNull, string[] whereNull)
        {
            if ((whereNotNull == null || whereNotNull.Length == 0) && (whereNull == null || whereNull.Length == 0))
            {
                return;
            }

            if (!acc.TryGetValue(indexName, out var lists))
            {
                lists = (new List<string>(), new List<string>());
                acc[indexName] = lists;
            }

            foreach (var name in whereNotNull ?? Array.Empty<string>())
            {
                if (!string.IsNullOrWhiteSpace(name) && !lists.NotNull.Contains(name)) lists.NotNull.Add(name);
            }
            foreach (var name in whereNull ?? Array.Empty<string>())
            {
                if (!string.IsNullOrWhiteSpace(name) && !lists.Null.Contains(name)) lists.Null.Add(name);
            }
        }

        /// <summary>
        /// Resolves merged predicate names to columns and refuses a declaration that cannot mean what it says
        /// (TASK-273).
        /// </summary>
        /// <remarks>
        /// <para>
        /// Three refusals, all at table load, all throwing rather than dropping — an attribute that silently
        /// does nothing leaves the model claiming a constraint the database does not have (§ SH-H037):
        /// an unmapped property name, a column this framework declares NOT NULL, and the same column in both
        /// lists. Fail-fast is affordable here only because the surface is new and nothing declares it yet;
        /// TASK-248 and TASK-256 both had to abandon the same instinct once their blast radius was measured.
        /// </para>
        /// <para>
        /// <b>The non-nullable test is <c>IsNotNull || IsPrimary</c>, and the second half is load-bearing.</b>
        /// <c>AbstractField.IsNotNull</c> is derived from the CLR type for value types, but for
        /// <c>string</c> and <c>byte[]</c> it is set <i>only</i> by <c>[RequiredField]</c> /
        /// <c>[Required]</c> — so a <c>string</c> primary key reads <c>IsNotNull == false</c>, and without
        /// the <c>IsPrimary</c> arm a <c>WhereNull</c> on it would be accepted and index zero rows.
        /// </para>
        /// <para>
        /// C# nullable-reference annotations are not read (<c>AbstractField</c> answers "nullable" for every
        /// reference type), so <c>string</c> and <c>string?</c> are indistinguishable and a
        /// <c>WhereNotNull</c> over an always-populated string is accepted as vacuous rather than refused.
        /// That is a limit of the metadata, not a decision, and it is documented on the attribute.
        /// </para>
        /// <para>
        /// Order is <c>WhereNotNull</c> terms then <c>WhereNull</c> terms, <b>each sorted ordinally by column
        /// name</b>. Sorted rather than in declaration order because accumulation follows
        /// <c>Type.GetProperties()</c>, whose order the CLR leaves unspecified — so with two contributing
        /// attributes the emitted text would be reproducible only by luck, and it is compared byte-for-byte.
        /// </para>
        /// </remarks>
        private static void ApplyIndexPredicates(
            Type type,
            Dictionary<string, IndexDefinition> indexes,
            Dictionary<string, (List<string> NotNull, List<string> Null)> predicateNames,
            Dictionary<string, AbstractField> fields)
        {
            foreach (var entry in predicateNames)
            {
                if (!indexes.TryGetValue(entry.Key, out var idx))
                {
                    // A predicate for an index name that contributed no columns cannot happen through the
                    // two loops above (both record names only after adding a column), so this is a guard
                    // against a future third caller rather than a reachable state.
                    throw new Exceptions.TableAttributeException(
                        $"Index '{entry.Key}' on {type.FullName}: a WhereNotNull/WhereNull predicate was declared for an index with no columns");
                }

                var contradiction = entry.Value.NotNull.FirstOrDefault(n => entry.Value.Null.Contains(n));
                if (contradiction != null)
                {
                    throw new Exceptions.TableAttributeException(
                        $"Index '{entry.Key}' on {type.FullName}: property '{contradiction}' is in both WhereNotNull and WhereNull, "
                        + "so the index would contain no rows at all");
                }

                // Sorted, not left in accumulation order: that order is Type.GetProperties()' order, which
                // the CLR does not specify, so with two [IndexedField] attributes contributing predicates the
                // emitted text would be stable only by luck — and the byte-for-byte assertions in the test
                // suite depend on it. Found at this task's close gate; the previous claim of "declaration
                // order" was incidental rather than enforced.
                foreach (var (name, requireNull) in entry.Value.NotNull.OrderBy(n => n, StringComparer.Ordinal).Select(n => (n, false))
                    .Concat(entry.Value.Null.OrderBy(n => n, StringComparer.Ordinal).Select(n => (n, true))))
                {
                    var field = fields.Values.FirstOrDefault(f => f.Property?.Name == name);
                    if (field == null || string.IsNullOrEmpty(field.Name))
                    {
                        throw new Exceptions.TableAttributeException(
                            $"Index '{entry.Key}' on {type.FullName}: {(requireNull ? "WhereNull" : "WhereNotNull")} names '{name}', which is not a mapped column");
                    }

                    if (field.IsNotNull || field.IsPrimary)
                    {
                        throw new Exceptions.TableAttributeException(
                            $"Index '{entry.Key}' on {type.FullName}: {(requireNull ? "WhereNull" : "WhereNotNull")} names '{name}', "
                            + $"which is declared NOT NULL{(field.IsPrimary ? " (primary key)" : "")}. "
                            + (requireNull
                                ? "The index would contain no rows."
                                : "The predicate would exclude nothing; remove the name."));
                    }

                    idx.Predicates.Add(new IndexPredicate { ColumnName = field.Name, RequireNull = requireNull });
                }
            }
        }

        public static int Read(DbDataReader reader, object data, int index = 0)
        {
            return Read(LoadFields(data.GetType()), reader, data, index);
        }

        public static Dictionary<string, object> Write(object data)
        {
            return Write(LoadFields(data.GetType()), data);
        }
    }
}
