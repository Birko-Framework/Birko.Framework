using System;
using System.Collections.Generic;
using System.Linq;
using Birko.Data.SQL.Conditions;
using Birko.Data.SQL.Fields;

namespace Birko.Data.SQL.Tables
{
    public class View
    {
        public string? Name { get; internal set; }
        public IEnumerable<Table> Tables { get; private set; } = null!;
        public IEnumerable<Conditions.Join>? Join { get; private set; }

        /// <summary>
        /// Controls how queries for this view are executed.
        /// </summary>
        public ViewQueryMode QueryMode { get; internal set; } = ViewQueryMode.OnTheFly;

        public View(IEnumerable<Table>? tables = null, IEnumerable<Conditions.Join>? join = null, string? name = null)
        {
            Tables = tables!;
            Join = join;
            Name = name;
            // CR-M147: derive a name from the table names ONLY when the caller supplied none — the
            // guard was inverted, so an explicit `name` was silently overwritten by the concatenation.
            if (string.IsNullOrEmpty(name) && Tables != null && Tables.Any())
            {
                Name = string.Join(string.Empty, Tables.Select(x => x.Name).Where(x => !string.IsNullOrEmpty(x)).Distinct());
            }
        }

        public View AddTable(Table table)
        {
            return AddTable(table.Name, table.Type, table.Fields.Values);
        }

        public View AddTable(string tableName, Type tableType, IEnumerable<Fields.AbstractField> fields)
        {
            if (fields != null && fields.Any())
            {
                foreach (var field in fields)
                {
                    AddField(tableName, tableType, field);
                }
            }
            return this;
        }

        /// <param name="name">
        /// Overrides the dictionary key. Leave null to use the field's <b>view property</b> — see the
        /// remarks on <see cref="ViewFieldKey"/> for why that, and not the source column, is the identity.
        /// </param>
        public View AddField(string tableName, Type tableType, AbstractField field, string? name = null)
        {
            if (!string.IsNullOrEmpty(tableName) && field != null)
            {
                Table? table = null;
                if (Tables != null && Tables.Any() && Tables.Any(x => x.Name == tableName))
                {
                    table = Tables.FirstOrDefault(x => x.Name == tableName)!;
                }
                else
                {
                    table = new Table()
                    {
                        Name = tableName,
                        Type = tableType
                    };
                    Tables = (Tables == null) ? new[] { table } : Tables.Concat(new[] { table });
                }
                if (table!.Fields == null)
                {
                    table.Fields = new Dictionary<string, AbstractField>();
                }
                var fieldName = (!string.IsNullOrEmpty(name)) ? name : ViewFieldKey(field);
                if (!table.Fields.TryGetValue(fieldName, out var existing))
                {
                    field.Table = table;
                    table.Fields.Add(fieldName, field);
                }
                else if (!IsSameField(existing, field))
                {
                    // TASK-207. The backstop, not the fix: keying by the view property above means the two
                    // reachable collision shapes cannot occur any more. This catches a key presented by a
                    // caller that supplied its own `name`, or by `AddTable`, whose source fields carry the
                    // source property rather than a view property.
                    throw new Birko.Data.Exceptions.FieldAttributeException(
                        $"View field key '{fieldName}' on table '{table.Name}' is already taken by "
                        + $"'{Describe(existing)}' and cannot also hold '{Describe(field)}'. "
                        + "Two view fields resolved to one key; the second would previously have been "
                        + "dropped with no column, no error and no log entry, reading back as default(T).");
                }
                // else: an idempotent re-add of a field already present. Three paths do this legitimately —
                // LoadView's `_fieldsCache` reuse branch, its multi-LoadField loop, and (the one that makes
                // reference equality useless here) its outer `foreach (ViewAttribute attr in attrs)` loop:
                // ViewAttribute is AllowMultiple = true, so a three-table view re-runs the whole per-property
                // field loop and re-presents every field as a FRESH AbstractField instance.
            }
            return this;
        }

        /// <summary>
        /// The key a view field is stored under: the <b>view property it populates</b>, falling back to the
        /// field's own name when no property is attached.
        /// <para>
        /// TASK-129 keyed aggregates this way after two <c>Sum</c>s on one table — both keyed <c>"SUM"</c>,
        /// the SQL function name — silently produced one column. It left non-aggregates keyed on
        /// <see cref="AbstractField.Name"/>, the <i>source column</i>, which kept two collision shapes alive
        /// (TASK-207): two view properties projecting one source column, and — newly, because the two
        /// namespaces then shared one key space — an aggregate whose view property happened to match a
        /// neighbouring column's source name. Both are gone once every field is identified by the property
        /// it populates, which is unique on a CLR type by construction.
        /// </para>
        /// <para>
        /// Safe to change because nothing reads this key as a column name: the persistent read
        /// (<see cref="GetPersistentViewSelectFields"/>) and the sort key both go through the field, the
        /// aggregate alias uses the key only as a fallback when <c>Property</c> is unset, and
        /// <c>Table.GetField(string)</c> has no callers.
        /// </para>
        /// </summary>
        private static string ViewFieldKey(AbstractField field)
            => field.Property != null ? field.Property.Name : field.Name;

        /// <summary>
        /// Whether an incoming field is the one already stored under its key — i.e. a re-add rather than a
        /// collision. Compared by value, not by reference: the multi-<c>[View]</c> loop re-presents every
        /// field as a new instance, so <c>ReferenceEquals</c> alone would report every such view as broken.
        /// </summary>
        private static bool IsSameField(AbstractField existing, AbstractField incoming)
            => ReferenceEquals(existing, incoming)
               || (existing.GetType() == incoming.GetType()
                   && existing.Name == incoming.Name
                   && existing.IsAggregate == incoming.IsAggregate
                   && existing.Property?.Name == incoming.Property?.Name
                   && existing.Property?.DeclaringType == incoming.Property?.DeclaringType);

        private static string Describe(AbstractField field)
            => field.Property != null
                ? $"{field.Property.DeclaringType?.Name}.{field.Property.Name} ({field.Name})"
                : field.Name;

        public View AddJoin(IEnumerable<Conditions.Join> conditions)
        {
            if (conditions != null && conditions.Any())
            {
                foreach (var condition in conditions)
                {
                    AddJoin(condition);
                }
            }
            return this;
        }

        public View AddJoin(Conditions.Join condition)
        {
            if (condition != null)
            {
                if (Join == null)
                {
                    Join = new[] { condition };
                }
                else if (Join.Any(x => x.Left == condition.Left && x.Right == condition.Right && x.JoinType == condition.JoinType))
                {
                    var join = Join.FirstOrDefault(x => x.Left == condition.Left && x.Right == condition.Right && x.JoinType == condition.JoinType);
                    if (join != null)
                    {
                        join.AddConditions(condition.Conditions);
                    }
                }
                else
                {
                    Join = Join.Concat(new[] { condition });
                }
            }
            return this;
        }

        /// <param name="aggregateAlias">
        /// Whether aggregate projections carry their <c>as &lt;ViewProperty&gt;</c> suffix. The DDL builder
        /// passes false and appends its own quoted alias instead (TASK-129) — see
        /// <see cref="Birko.Data.SQL.Tables.Table.GetSelectFields"/>.
        /// </param>
        public IDictionary<int, string> GetSelectFields(bool notAggregate = false, bool aggregateAlias = true)
        {
            var result = new Dictionary<int, string>();
            int i = 0;
            foreach (var table in Tables)
            {
                var fields = table?.GetSelectFields(true, notAggregate, aggregateAlias);
                if (fields != null && fields.Any())
                {
                    foreach (var field in fields)
                    {
                        result.Add(i, field.Value);
                        i++;
                    }
                }
            }
            return result;
        }

        internal IEnumerable<AbstractField> GetTableFields(bool notAggregate = false)
        {
            List<AbstractField> tableFields = new List<AbstractField>();
            foreach (var table in Tables.Where(x => x != null))
            {
                tableFields.AddRange(table.GetTableFields(notAggregate));
            }
            return tableFields;
        }

        public bool HasAggregateFields()
        {
            return Tables?.Any(x => x?.HasAggregateFields() ?? false) ?? false;
        }

        /// <summary>
        /// Gets column names for querying a persistent view (no table prefix, just column names).
        /// Non-aggregate columns use the field Name (the source column). Aggregate columns use the
        /// view-property name to match the AS alias emitted by <c>ViewSelectSqlBuilder</c> — the aggregate
        /// function name would collide across two aggregates of the same function (CR-L195).
        /// </summary>
        public IDictionary<int, string> GetPersistentViewSelectFields()
        {
            var result = new Dictionary<int, string>();
            int i = 0;
            foreach (var table in Tables)
            {
                if (table?.Fields == null)
                {
                    continue;
                }

                foreach (var field in table.Fields.Values)
                {
                    result.Add(i, field.IsAggregate ? field.Property.Name : field.Name);
                    i++;
                }
            }
            return result;
        }
    }
}
