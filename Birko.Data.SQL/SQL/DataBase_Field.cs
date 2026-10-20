using Birko.Data.SQL.Fields;
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
        internal static readonly ConcurrentDictionary<Type, IEnumerable<Fields.AbstractField>> _fieldsCache = new();

        // TASK-691: lengths a fluent mapping declares, read by LoadField as a MaxLengthField after the property's own
        // attributes, so CreateAbstractField stays the one producer of a column's type.
        private static readonly ConcurrentDictionary<(Type Model, string Property), int> _maxLengthOverrides = new();

        /// <summary>
        /// Bounds <paramref name="propertyName"/> of <paramref name="modelType"/> to <paramref name="maxLength"/>, as a
        /// <c>[MaxLengthField]</c> on the property would, and winning over one (used by fluent mapping systems:
        /// <c>HasMaxLength</c>). Register before any store loads the type, as <see cref="RegisterTableName(Type, string)"/>
        /// requires.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Registering a different length drops the type's cached fields and table, so the next load builds the bounded
        /// field. Registering the same length again changes nothing, so the flags a mapping applied to the loaded fields stay.
        /// Like <see cref="RegisterTableName(Type, string)"/>, a registration is process-wide and is meant for startup: a
        /// change drops what other registrations wrote onto the cached table, and a load running at the same moment can
        /// cache the field it built before the change.
        /// </para>
        /// <para>
        /// A length registered for a type also bounds that property on every type derived from it, as an inherited
        /// attribute would; the most derived registration wins.
        /// </para>
        /// </remarks>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxLength"/> is not positive.</exception>
        public static void RegisterMaxLength(Type modelType, string propertyName, int maxLength)
        {
            ArgumentNullException.ThrowIfNull(modelType);
            ArgumentException.ThrowIfNullOrEmpty(propertyName);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxLength);
            var key = (modelType, propertyName);
            if (_maxLengthOverrides.TryGetValue(key, out var registered) && registered == maxLength)
            {
                return;
            }
            _maxLengthOverrides[key] = maxLength;
            _fieldsCache.TryRemove(modelType, out _);
            _tableCache.TryRemove(modelType, out _);
        }

        internal static IEnumerable<AbstractField> LoadFields(Type type)
        {
            return _fieldsCache.GetOrAdd(type, t =>
            {
                List<AbstractField> list = new List<AbstractField>();
                GetProperties(t, (field) =>
                {
                    list.AddRange(LoadField(field));
                });
                return list.ToArray();
            });
        }

        private static bool IsForeignFieldAttribute(Type type)
        {
            for (var t = type; t != null && t != typeof(object); t = t.BaseType)
            {
                if (t.FullName == "Birko.Data.SQL.Attributes.Field")
                    return true;
            }
            return false;
        }

        public static IEnumerable<AbstractField> LoadField(PropertyInfo field) => LoadField(field, null);

        /// <summary>
        /// <see cref="LoadField(PropertyInfo)"/> for <paramref name="field"/> read through <paramref name="modelType"/>, so a
        /// length registered for the model applies even when the property is declared on a base type (a lambda's member).
        /// </summary>
        public static IEnumerable<AbstractField> LoadField(PropertyInfo field, Type? modelType)
        {
            // Direct cast works when attribute type identity matches (same compilation unit).
            var directAttrs = field.GetCustomAttributes(typeof(Birko.Data.SQL.Attributes.Field), true);
            Birko.Data.SQL.Attributes.Field[] fieldAttrs;

            if (directAttrs.Length > 0)
            {
                fieldAttrs = (Birko.Data.SQL.Attributes.Field[])directAttrs;
            }
            else
            {
                // Cross-assembly shared-project fallback: attribute compiled into a different assembly
                // has a different CLR type identity. Reconstruct equivalent local attributes via reflection.
                // TASK-651: only a foreign FIELD attribute (one whose type derives, by name, from Attributes.Field). The
                // namespace test also caught [ViewField] / [AggregateField], which live there without being fields, and
                // a property carrying only one of those threw "comes from another assembly".
                var crossAttrs = field.GetCustomAttributes(true)
                    .Where(a => !(a is Birko.Data.SQL.Attributes.Field) && IsForeignFieldAttribute(a.GetType()))
                    .ToList();

                if (crossAttrs.Count == 0)
                {
                    fieldAttrs = Array.Empty<Birko.Data.SQL.Attributes.Field>();
                }
                else
                {
                    var rebuilt = new List<Birko.Data.SQL.Attributes.Field>();
                    foreach (var attr in crossAttrs)
                    {
                        rebuilt.Add(RebuildForeignFieldAttribute(attr, field));
                    }
                    fieldAttrs = rebuilt.ToArray();
                }
            }

            if (RegisteredMaxLength(field, modelType) is int maxLength)
            {
                // Last, so it wins over the property's own MaxLengthField (CreateAbstractField: the last one read wins).
                fieldAttrs = fieldAttrs.Append(new Birko.Data.SQL.Attributes.MaxLengthField(maxLength)).ToArray();
            }

            var tableField = Fields.AbstractField.CreateAbstractField(field, fieldAttrs);
            return tableField != null ? new[] { tableField } : Array.Empty<AbstractField>();
        }

        /// <summary>
        /// Rebuilds a field attribute compiled into another assembly (the split-aggregator layout) as the local type,
        /// with every value it carries (TASK-577). The rebuild used to read <c>MaxLength</c>, <c>Precision</c> and
        /// <c>Scale</c> with <c>GetProperty</c> although they are public FIELDS, so each came back 0; it dropped
        /// <c>[UtcField]</c> in a <c>default: skip</c>, so an instant column became a wall clock; and it left out
        /// <c>IndexedField.IsUnique</c> and its predicate lists. Every value is now read as whichever member the
        /// attribute declares, and an attribute this method does not know throws rather than being skipped, so a new
        /// attribute type cannot work in-assembly and silently do nothing cross-assembly.
        /// </summary>
        private static Birko.Data.SQL.Attributes.Field RebuildForeignFieldAttribute(object attr, PropertyInfo property)
        {
            var type = attr.GetType();
            switch (type.Name)
            {
                case "PrimaryField": return new Birko.Data.SQL.Attributes.PrimaryField();
                case "UniqueField": return new Birko.Data.SQL.Attributes.UniqueField();
                case "RequiredField": return new Birko.Data.SQL.Attributes.RequiredField();
                case "IncrementField": return new Birko.Data.SQL.Attributes.IncrementField();
                case "IgnoreField": return new Birko.Data.SQL.Attributes.IgnoreField();
                case "UtcField": return new Birko.Data.SQL.Attributes.UtcField();
                case "NamedField": return new Birko.Data.SQL.Attributes.NamedField((string?)ForeignValue(attr, "Name", property));
                case "MaxLengthField": return new Birko.Data.SQL.Attributes.MaxLengthField((int)ForeignValue(attr, "MaxLength", property)!);
                case "PrecisionField": return new Birko.Data.SQL.Attributes.PrecisionField((int)ForeignValue(attr, "Precision", property)!);
                case "ScaleField": return new Birko.Data.SQL.Attributes.ScaleField((int)ForeignValue(attr, "Scale", property)!);
                case "IndexedField":
                    return new Birko.Data.SQL.Attributes.IndexedField(
                        (string)ForeignValue(attr, "Name", property)!,
                        (int)ForeignValue(attr, "Order", property)!,
                        (bool)ForeignValue(attr, "IsDescending", property)!,
                        (bool)ForeignValue(attr, "IsUnique", property)!)
                    {
                        WhereNotNull = (string[])ForeignValue(attr, "WhereNotNull", property)!,
                        WhereNull = (string[])ForeignValue(attr, "WhereNull", property)!,
                    };
                default:
                    throw new Exceptions.FieldAttributeException(
                        $"{property.DeclaringType?.FullName}.{property.Name}: attribute {type.FullName} comes from another "
                        + $"assembly ({type.Assembly.GetName().Name}) and this build of Birko.Data.SQL cannot rebuild it. "
                        + "Compile both from the same Birko.Data.SQL sources.");
            }
        }

        /// <summary>A foreign attribute's value, read as a property or a field, whichever the attribute declares.</summary>
        private static object? ForeignValue(object attr, string member, PropertyInfo property)
        {
            var type = attr.GetType();
            var prop = type.GetProperty(member, BindingFlags.Public | BindingFlags.Instance);
            if (prop != null) return prop.GetValue(attr);
            var fieldInfo = type.GetField(member, BindingFlags.Public | BindingFlags.Instance);
            if (fieldInfo != null) return fieldInfo.GetValue(attr);
            throw new Exceptions.FieldAttributeException(
                $"{property.DeclaringType?.FullName}.{property.Name}: attribute {type.FullName} from assembly "
                + $"{type.Assembly.GetName().Name} has no '{member}'; it was compiled from different Birko.Data.SQL sources.");
        }

        /// <summary>
        /// SH-M317 (TASK-159). A member reached through an interface (<c>x =&gt; x.UpdatedBy</c> where T is constrained to
        /// <c>IAuditable</c>, as in the Audit decorator) binds to the INTERFACE property, which carries none of the model's
        /// attributes, so a <c>[NamedField]</c> remap was ignored and the SET named a column that does not exist. The member
        /// is resolved by name on the model type instead, most-derived first; an explicit implementation is not found by
        /// name and keeps the interface property.
        /// </summary>
        /// <summary>
        /// The length registered for <paramref name="field"/>: looked up on <paramref name="modelType"/> and its base types,
        /// then on the type it was reflected from and its base types; the first (most derived) registration wins.
        /// </summary>
        private static int? RegisteredMaxLength(PropertyInfo field, Type? modelType)
        {
            if (_maxLengthOverrides.IsEmpty)
            {
                return null;
            }
            foreach (var start in new[] { modelType, field.ReflectedType })
            {
                for (var type = start; type != null && type != typeof(object); type = type.BaseType)
                {
                    if (_maxLengthOverrides.TryGetValue((type, field.Name), out var length))
                    {
                        return length;
                    }
                }
            }
            return null;
        }

        private static PropertyInfo ResolveOnModel(PropertyInfo property, Type modelType)
        {
            if (property.DeclaringType?.IsInterface != true || modelType.IsInterface)
            {
                return property;
            }
            for (var type = modelType; type != null; type = type.BaseType)
            {
                var declared = type.GetProperty(property.Name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
                if (declared != null)
                {
                    return declared;
                }
            }
            return property;
        }

        public static AbstractField GetField<T, P>(Expression<Func<T, P>> expr)
        {
            PropertyInfo? propInfo = null;
            if (expr.Body is UnaryExpression expression)
            {
                propInfo = (expression.Operand as MemberExpression)?.Member as PropertyInfo;
            }
            else if(expr.Body is MemberExpression memberExpression)
            {
                propInfo = memberExpression.Member as PropertyInfo;
            }
            if (propInfo == null)
            {
                throw new ArgumentException($"Unable to resolve property from expression: {expr}", nameof(expr));
            }
            propInfo = ResolveOnModel(propInfo, expr.Parameters[0].Type);
            if (propInfo.ReflectedType == typeof(Models.AbstractLogModel))
            {
                propInfo = typeof(Models.AbstractDatabaseLogModel).GetProperty(propInfo.Name);
            }
            else if (propInfo.ReflectedType == typeof(Models.AbstractModel))
            {
                propInfo = typeof(Models.AbstractDatabaseModel).GetProperty(propInfo.Name);
            }
            var fields = LoadField(propInfo!, expr.Parameters[0].Type);
            return fields.First();
        }

        /// <summary>
        /// Resolves an AbstractField from a non-generic LambdaExpression.
        /// Used by bulk store PropertyUpdate where generic type args are erased.
        /// </summary>
        public static AbstractField GetFieldFromLambda(LambdaExpression expr)
        {
            PropertyInfo? propInfo = null;
            if (expr.Body is UnaryExpression expression)
            {
                propInfo = (expression.Operand as MemberExpression)?.Member as PropertyInfo;
            }
            else if (expr.Body is MemberExpression memberExpression)
            {
                propInfo = memberExpression.Member as PropertyInfo;
            }
            if (propInfo == null)
            {
                throw new ArgumentException($"Unable to resolve property from expression: {expr}", nameof(expr));
            }
            propInfo = ResolveOnModel(propInfo, expr.Parameters[0].Type);
            if (propInfo.ReflectedType == typeof(Models.AbstractLogModel))
            {
                propInfo = typeof(Models.AbstractDatabaseLogModel).GetProperty(propInfo.Name);
            }
            else if (propInfo.ReflectedType == typeof(Models.AbstractModel))
            {
                propInfo = typeof(Models.AbstractDatabaseModel).GetProperty(propInfo.Name);
            }
            var fields = LoadField(propInfo!, expr.Parameters[0].Type);
            return fields.FirstOrDefault()
                // SH-M027 (TASK-170): an [IgnoreField] / [NotMapped] / unsupported member maps to no field, and First()
                // threw "Sequence contains no elements" from inside the store, naming nothing.
                ?? throw new ArgumentException($"'{expr}' names {propInfo!.DeclaringType?.Name}.{propInfo.Name}, which is not a mapped column.", nameof(expr));
        }

        public static IEnumerable<AbstractField> GetPrimaryFields(Type type)
        {
            var table = TryLoadTable(type);
            var primaries = table?.GetPrimaryFields()?.ToList();

            // Fallback: if no [PrimaryField] attribute is declared, use the Guid property
            // (all AbstractModel descendants have Guid). This prevents UPDATE/DELETE
            // without a WHERE clause for platform-independent models.
            if ((primaries == null || primaries.Count == 0) && table != null)
            {
                var guidField = table.GetFieldByPropertyName("Guid");
                if (guidField != null)
                    return new[] { guidField };
            }

            return (IEnumerable<AbstractField>?)primaries ?? Array.Empty<AbstractField>();
        }

        public static int Read(IEnumerable<Fields.AbstractField> fields, DbDataReader reader, object data, int index = 0)
        {
            if (fields != null)
            {
                foreach (var tableField in fields)
                {
                    tableField.Read(data, reader, index);
                    index++;
                }
            }
            return index;
        }

        public static Dictionary<string, object> Write(IEnumerable<Fields.AbstractField> fields, object data)
        {
            Dictionary<string, object> result = new Dictionary<string, object>();
            if (fields != null)
            {
                foreach (var tableField in fields)
                {
                    result.Add(tableField.Name, tableField.Write(data)!);
                }
            }
            return result;
        }
    }
}
