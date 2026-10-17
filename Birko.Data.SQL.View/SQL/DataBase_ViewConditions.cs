using Birko.Data.SQL.Fields;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Birko.Data.SQL
{
    public static partial class DataBase
    {
        /// <summary>
        /// Resolves a view query's WHERE conditions for the path it runs on: the twin of
        /// <see cref="ResolveViewOrderFields"/>, called beside it once <c>usePersistent</c> is known.
        /// </summary>
        /// <remarks>
        /// <para>
        /// TASK-639. Conditions arrive named for the on-the-fly SELECT, the source column (<c>Table.Column</c> or
        /// <c>Column</c>), because that is what a filter on a view property resolves to. A persistent view exposes its
        /// columns under the view property names instead, so the same WHERE asked a VIEW for a column it does not have:
        /// measured on SQLite, PostgreSQL, MySQL and SQL Server, every filtered read and count of a Persistent view, and
        /// of an Auto view once it existed, failed ("no such column: T639Persons.Name", 42P01, …).
        /// </para>
        /// <para>
        /// On the persistent path each source name a field of the view knows is replaced by that field's column there,
        /// the view property (as <see cref="ViewOrderFieldName"/> and <c>GetPersistentViewSelectFields</c> spell it),
        /// both for a condition's name and for the other column of a field-to-field comparison. A name that is already a
        /// persistent column, or that no field knows, is left as it is. An unqualified source name two fields share with
        /// different columns is refused, since picking one would filter on the wrong column. The caller's conditions are
        /// not modified.
        /// </para>
        /// </remarks>
        /// <exception cref="ArgumentException">A condition names an unqualified source column two view fields share.</exception>
        public static IEnumerable<Conditions.Condition>? ResolveViewConditions(Tables.View? view, IEnumerable<Conditions.Condition>? conditions, bool persistent)
        {
            if (!persistent || view == null || conditions == null)
            {
                return conditions;
            }

            var fields = view.GetTableFields().Where(f => f != null).ToArray();
            var columns = new HashSet<string>(fields.Select(PersistentViewColumn), StringComparer.Ordinal);
            var map = new Dictionary<string, string?>(StringComparer.Ordinal);
            foreach (var field in fields)
            {
                var column = PersistentViewColumn(field);
                foreach (var source in new[] { field.GetSelectName(true), field.GetSelectName(false) })
                {
                    if (columns.Contains(source) && source != column)
                    {
                        // Already another field's persistent column: that reading wins, as it did before.
                        continue;
                    }
                    map[source] = map.TryGetValue(source, out var known) && known != column ? null : column;
                }
            }

            return conditions.Select(c => ResolveViewCondition(view, c, map)).ToList();
        }

        private static string PersistentViewColumn(AbstractField field)
            => field.Property != null ? field.Property.Name : field.Name;

        private static Conditions.Condition ResolveViewCondition(Tables.View view, Conditions.Condition condition, IReadOnlyDictionary<string, string?> map)
        {
            var values = condition.Values;
            if (condition.IsField && values != null)
            {
                values = values.Cast<object?>().Select(v => v is string other ? ResolveViewColumn(view, other, map) : v).ToList();
            }

            return new Conditions.Condition(
                condition.Name == null ? null : ResolveViewColumn(view, condition.Name, map),
                values,
                condition.Type,
                condition.IsField,
                condition.IsNot,
                condition.IsOr,
                condition.SubConditions?.Select(c => ResolveViewCondition(view, c, map)).ToList());
        }

        private static string ResolveViewColumn(Tables.View view, string name, IReadOnlyDictionary<string, string?> map)
        {
            if (!map.TryGetValue(name, out var column))
            {
                return name;
            }
            return column ?? throw new ArgumentException(
                $"View '{view.Name}': the filter column '{name}' belongs to more than one of the view's fields; filter on the view property instead.",
                nameof(name));
        }
    }
}
