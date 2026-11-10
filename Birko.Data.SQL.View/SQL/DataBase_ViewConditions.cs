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
        /// <para>
        /// TASK-858: a resolved condition is a <see cref="Conditions.Condition.Copy"/>, so it carries every property of the
        /// one it replaces, except the value-fragment re-renders. Those render the SOURCE columns, which is right only on the
        /// OnTheFly path. A fragment over a plain column is that column, and is renamed like one. A fragment that computes
        /// (arithmetic, <c>COALESCE</c>, <c>CASE</c>, <c>Math.Round</c>) is refused by name before anything runs: its columns
        /// cannot be renamed. Measured before, it ran its portable form against the view, and every provider failed with an
        /// error that named something else ("no such column: T858Orders.Amount", 42P01 "missing FROM-clause entry", and on SQL
        /// Server "'BIRKO_ROUND_TOEVEN' is not a recognized built-in function name").
        /// </para>
        /// <para>
        /// TASK-890: a case fold (<c>ToLower()</c>, <c>ToUpper()</c>, their invariant forms) of a plain column is the one
        /// fragment whose column is renamed (<see cref="Conditions.Condition.CaseFold"/>): it folds the view's column through the
        /// provider's <see cref="Conditions.SqlValueFragmentDialect.CaseFold"/>. Since TASK-870 it had been refused by name. A
        /// fold of a computed value is still refused.
        /// </para>
        /// </remarks>
        /// <exception cref="ArgumentException">A condition names an unqualified source column two view fields share.</exception>
        /// <exception cref="NotSupportedException">A condition computes a value over the view's source columns.</exception>
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

            return conditions.Select(c => ResolveViewCondition(view, c, map, columns)).ToList();
        }

        private static string PersistentViewColumn(AbstractField field)
            => field.Property != null ? field.Property.Name : field.Name;

        private static Conditions.Condition ResolveViewCondition(Tables.View view, Conditions.Condition condition,
            IReadOnlyDictionary<string, string?> map, ISet<string> columns)
        {
            var values = condition.Values;
            if (condition.IsField && values != null)
            {
                if (condition.FieldFragment != null)
                {
                    foreach (var other in values.OfType<string>())
                        RequireViewColumn(view, other, map, columns);
                }
                values = values.Cast<object?>().Select(v => v is string other ? ResolveViewColumn(view, other, map) : v).ToList();
            }
            // TASK-890: a case fold of a plain column folds the view's column instead.
            var fold = condition.CaseFold != null && condition.NameFragment != null && condition.Name == FoldedName(condition.CaseFold)
                ? condition.CaseFold
                : null;
            if (fold != null)
            {
                RequireViewColumn(view, fold.Column, map, columns);
            }
            else if (condition.NameFragment != null && condition.Name != null)
            {
                RequireViewColumn(view, condition.Name, map, columns);
            }

            var resolved = condition.Copy(values, condition.SubConditions?.Select(c => ResolveViewCondition(view, c, map, columns)).ToList());
            resolved.Name = condition.Name == null ? null : ResolveViewColumn(view, condition.Name, map);
            resolved.NameFragment = null;
            resolved.FieldFragment = null;
            resolved.CaseFold = null;
            if (fold != null)
            {
                FoldColumn(resolved, new Conditions.CaseFoldOperand(ResolveViewColumn(view, fold.Column, map), fold.Upper, fold.Invariant));
            }
            return resolved;
        }

        /// <summary>TASK-858 — refuses a value fragment that is not a plain column of the view or of its source.</summary>
        private static void RequireViewColumn(Tables.View view, string name, IReadOnlyDictionary<string, string?> map, ISet<string> columns)
        {
            if (map.ContainsKey(name) || columns.Contains(name))
            {
                return;
            }
            throw new NotSupportedException(
                $"View '{view.Name}': the filter computes '{name}' over the view's source columns, which a persistent view does not "
                + "have. Compare a view property with a value or with another property, or query the view OnTheFly.");
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
