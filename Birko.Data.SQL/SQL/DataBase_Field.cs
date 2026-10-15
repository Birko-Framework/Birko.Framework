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

        public static IEnumerable<AbstractField> LoadField(PropertyInfo field)
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
                var crossAttrs = field.GetCustomAttributes(true)
                    .Where(a => a.GetType().FullName?.StartsWith("Birko.Data.SQL.Attributes.") == true
                             && !(a is Birko.Data.SQL.Attributes.Field))
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
            if (propInfo.ReflectedType == typeof(Models.AbstractLogModel))
            {
                propInfo = typeof(Models.AbstractDatabaseLogModel).GetProperty(propInfo.Name);
            }
            else if (propInfo.ReflectedType == typeof(Models.AbstractModel))
            {
                propInfo = typeof(Models.AbstractDatabaseModel).GetProperty(propInfo.Name);
            }
            var fields = LoadField(propInfo!);
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
            if (propInfo.ReflectedType == typeof(Models.AbstractLogModel))
            {
                propInfo = typeof(Models.AbstractDatabaseLogModel).GetProperty(propInfo.Name);
            }
            else if (propInfo.ReflectedType == typeof(Models.AbstractModel))
            {
                propInfo = typeof(Models.AbstractDatabaseModel).GetProperty(propInfo.Name);
            }
            var fields = LoadField(propInfo!);
            return fields.First();
        }

        public static IEnumerable<AbstractField> GetPrimaryFields(Type type)
        {
            var table = LoadTable(type);
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
