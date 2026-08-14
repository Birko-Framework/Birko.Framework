using Birko.Data.SQL.Attributes;
using Birko.Data.SQL.Conditions;
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
        // Concurrent to match the sibling _fieldsCache; a plain Dictionary mutated from the static
        // LoadView across concurrent requests could corrupt its buckets (CR-H094).
        private static readonly ConcurrentDictionary<Type, Tables.View> _viewCache = new();

        /// <summary>
        /// Registers the view field resolver at module load, not merely on the first <see cref="LoadView"/>.
        /// <para>
        /// TASK-111. `DataBase.ResolveRuleField` resolves a rule's field **at the caller**, before any store
        /// operation has run — unlike `ResolveOrderFields`, which the connector invokes after `LoadView` has
        /// already registered this delegate. A view type carries no `[Table]`, so `LoadTable` returns null
        /// and the delegate is the only thing that can resolve its fields. Without this, the *first*
        /// `ToConditions&lt;MyView&gt;(…)` in a process threw `ArgumentException` for a perfectly valid view
        /// field, and the identical call succeeded later once anything had touched a view — an
        /// order-dependent, first-call-only failure, which is the worst kind to diagnose.
        /// </para>
        /// <para>
        /// Shared projects compile into each consuming assembly, so this runs once per module;
        /// <see cref="EnsureViewResolverRegistered"/> is idempotent and no-ops on every call after the first.
        /// </para>
        /// </summary>
        [System.Runtime.CompilerServices.ModuleInitializer]
        internal static void InitializeViewResolver() => EnsureViewResolverRegistered();

        private static void EnsureViewResolverRegistered()
        {
            if (ResolveFieldSelectName == null)
            {
                ResolveFieldSelectName = (type, propertyName, withTableName) =>
                {
                    var view = LoadView(type);
                    if (view != null)
                    {
                        var field = view.GetTableFields().FirstOrDefault(x => x.Property.Name == propertyName);
                        if (field != null)
                        {
                            return field.GetSelectName(withTableName);
                        }
                    }
                    return null;
                };
            }
        }

        public static IEnumerable<Tables.View> LoadViews(IEnumerable<Type> types)
        {
            if (types != null && types.Any())
            {
                List<Tables.View> tables = new List<Tables.View>();
                foreach (Type type in types)
                {
                    var table = LoadView(type);
                    if (table != null && table.Tables != null && table.Tables.Any() && table.Tables.Any(x => x.Fields != null && x.Fields.Any()))
                    {
                        tables.Add(table);
                    }
                }
                return tables.ToArray();
            }
            else
            {
                throw new Exceptions.TableAttributeException("Types enumerable is empty ot null");
            }
        }

        public static AbstractField GetViewField<T, P>(Expression<Func<T, P>> expr)
        {
            // CR-M148: accept a plain MemberExpression body (e.g. x => x.Name on a reference type, no
            // boxing conversion) as well as a UnaryExpression(Operand) — the unconditional
            // (UnaryExpression) cast threw InvalidCastException for the former.
            var member = expr.Body as MemberExpression
                ?? (expr.Body as UnaryExpression)?.Operand as MemberExpression;
            if (member?.Member is not PropertyInfo propInfo)
            {
                throw new ArgumentException($"Expression '{expr}' must reference a property.", nameof(expr));
            }

            object[] fieldAttrs = propInfo.GetCustomAttributes(typeof(ViewFieldAttribute), true);
            if (fieldAttrs != null && fieldAttrs.Any())
            {
                foreach (ViewFieldAttribute fieldAttr in fieldAttrs)
                {
                    var table = LoadTable(fieldAttr.ModelType);
                    if (table != null)
                    {
                        return table.GetFieldByPropertyName(fieldAttr.ModelProperyName!)!;
                    }
                }
            }

            // CR-M148: fail with a descriptive error rather than returning null! (callers dereference
            // the result immediately, so null! surfaced as an opaque NullReferenceException).
            throw new InvalidOperationException(
                $"Property '{propInfo.Name}' has no [ViewField] mapping (or its source table could not be loaded).");
        }

        public static Tables.View LoadView(Type type)
        {
            EnsureViewResolverRegistered();
            if (!_viewCache.ContainsKey(type))
            {
                object[] attrs = type.GetCustomAttributes(typeof(ViewAttribute), true).ToArray();
                if (attrs != null)
                {
                    Tables.View view = new Tables.View();
                    foreach (ViewAttribute attr in attrs)
                    {
                        var tableLeft = attr.ModelLeft != null ? LoadTable(attr.ModelLeft) : null;
                        var tableRight = attr.ModelRight != null ? LoadTable(attr.ModelRight) : null;
                        if (tableLeft != null && tableRight != null)
                        {
                            var fieldLeft = (attr.ModelProperyLeft is string) ? tableLeft.GetFieldByPropertyName((string)attr.ModelProperyLeft) : null;
                            var fieldRight = (attr.ModelProperyRight is string) ? tableRight?.GetFieldByPropertyName((string)attr.ModelProperyRight) ?? null : null;
                            if (fieldLeft != null || fieldRight != null)
                            {
                                var joinType = JoinType.Cross;
                                switch (attr.Connect)
                                {
                                    case ViewConnect.Check: joinType = JoinType.LeftOuter;  break;
                                    case ViewConnect.CheckExisting: joinType = JoinType.Inner; break;
                                }

                                Conditions.Condition? cond = null;
                                if (fieldLeft != null && fieldRight != null)
                                {
                                    cond = Conditions.Condition.AndField(tableLeft.Name + "." + fieldLeft.Name, tableRight!.Name + "." + fieldRight.Name);
                                }
                                else if (fieldLeft != null && fieldRight == null)
                                {
                                    cond = Conditions.Condition.AndValue(tableLeft.Name + "." + fieldLeft.Name, attr.ModelProperyRight);
                                }
                                else if (fieldLeft == null && fieldRight != null)
                                {
                                    cond = Conditions.Condition.AndValue(tableRight!.Name + "." + fieldRight.Name, attr.ModelProperyLeft);
                                }
                                if (cond != null)
                                {
                                    view.AddJoin(Join.Create(tableLeft.Name, tableRight!.Name, cond, joinType));
                                }
                            }
                        }
                        foreach (var field in type.GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
                        {
                            object[] fieldAttrs = field.GetCustomAttributes(typeof(ViewFieldAttribute), true);
                            if (fieldAttrs != null && fieldAttrs.Any())
                            {
                                foreach (ViewFieldAttribute fieldAttr in fieldAttrs)
                                {
                                    var table = LoadTable(fieldAttr.ModelType);
                                    if (table != null)
                                    {
                                        string name = !string.IsNullOrEmpty(fieldAttr.ModelProperyName) ? fieldAttr.ModelProperyName : field.Name;
                                        if (_fieldsCache.ContainsKey(type) && _fieldsCache[type].Any(x => x.Name == name))
                                        {
                                            view.AddField(table.Name, table.Type, _fieldsCache[type].FirstOrDefault(x => x.Name == name)!);
                                        }
                                        else
                                        {
                                            var tableField = table.GetFieldByPropertyName(fieldAttr.ModelProperyName!);
                                            if (tableField != null)
                                            {
                                                if (fieldAttr is AggregateFieldAttribute)
                                                {
                                                    var functionField = FunctionField.CreateFunctionAggregateField(field, (AggregateFieldAttribute)fieldAttr, tableField);
                                                    if (functionField != null)
                                                    {
                                                        tableField = functionField;
                                                    }
                                                    // Keyed by the VIEW PROPERTY (`field`), not by tableField.Name — which after the
                                                    // reassignment above is the SQL function name. TASK-129: two same-function
                                                    // aggregates both keyed "COUNT" silently lost the second column. TASK-207 made
                                                    // the view property the DEFAULT key for every view field, so this argument is now
                                                    // explicit agreement with View.AddField rather than the thing preventing the
                                                    // collision. (TASK-129's "unique by construction" was true only among view
                                                    // properties — non-aggregates beside them were keyed by source column, so the two
                                                    // namespaces could collide. TASK-207 closed that.)
                                                    //
                                                    // This replaces a dead `tableFieldName` local that concatenated
                                                    // `tableField.Name + functionField.Name` and was then never passed — an abandoned
                                                    // attempt at this same uniqueness.
                                                    view.AddField(table.Name, table.Type, tableField, field.Name);
                                                }
                                                else
                                                {
                                                    var loadedFields = LoadField(tableField.Property);
                                                    foreach (var loadField in loadedFields)
                                                    {
                                                        loadField.Property = field;
                                                        view.AddField(table.Name, table.Type, loadField);
                                                    }
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                    if (view.Tables != null)
                    {
                        // Copy QueryMode and Name from the first ViewAttribute that has them set
                        var firstAttr = attrs.Cast<ViewAttribute>().FirstOrDefault();
                        if (firstAttr != null)
                        {
                            view.QueryMode = firstAttr.QueryMode;
                            if (!string.IsNullOrEmpty(firstAttr.Name))
                            {
                                view.Name = firstAttr.Name;
                            }
                        }
                        // If Name is still null, derive from table names
                        if (string.IsNullOrEmpty(view.Name) && view.Tables.Any())
                        {
                            view.Name = string.Join(string.Empty, view.Tables.Select(x => x.Name).Where(x => !string.IsNullOrEmpty(x)).Distinct());
                        }

                        _fieldsCache.TryAdd(type, view.Tables.SelectMany(x => x.Fields.Values).ToArray());
                        _viewCache[type] = view;
                    }
                    else
                    {
                        return null!;
                    }
                }
                else
                {
                    throw new Exceptions.TableAttributeException("No view attributes in type");
                }
            }
            return _viewCache[type];
        }

        public static int ReadView(DbDataReader reader, object data, int index = 0)
        {
            var type = data.GetType();
            var view = LoadView(type);
            // LoadView can return null (the view.Tables == null branch); guard rather than deferring a
            // NullReferenceException into Read via a null-forgiving `!` (CR-L197).
            if (view == null)
            {
                throw new Exceptions.TableAttributeException($"No view definition for type '{type.Name}'.");
            }
            return Read(view.GetTableFields(), reader, data, index);
        }
    }
}
