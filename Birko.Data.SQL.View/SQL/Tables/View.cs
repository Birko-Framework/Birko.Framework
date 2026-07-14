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
                var fieldName = (!string.IsNullOrEmpty(name)) ? name : field.Name;
                if (!table.Fields.ContainsKey(fieldName))
                {
                    field.Table = table;
                    table.Fields.Add(fieldName, field);
                }
            }
            return this;
        }

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

        public IDictionary<int, string> GetSelectFields(bool notAggregate = false)
        {
            var result = new Dictionary<int, string>();
            int i = 0;
            foreach (var table in Tables)
            {
                var fields = table?.GetSelectFields(true, notAggregate);
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
