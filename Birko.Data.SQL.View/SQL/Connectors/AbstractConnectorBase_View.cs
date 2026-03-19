using System;
using System.Collections.Generic;
using System.Linq;

namespace Birko.Data.SQL.Connectors
{
    public abstract partial class AbstractConnectorBase
    {
        /// <summary>
        /// Builds the CREATE VIEW SQL statement.
        /// Override in database-specific connectors for syntax differences.
        /// Default uses CREATE OR REPLACE VIEW (PostgreSQL, MySQL compatible).
        /// </summary>
        protected virtual string BuildCreateViewSql(string viewName, string selectSql)
        {
            return "CREATE OR REPLACE VIEW " + QuoteIdentifier(viewName) + " AS " + selectSql;
        }

        /// <summary>
        /// Builds the SELECT SQL that defines a view's body (no WHERE, ORDER BY, LIMIT, OFFSET).
        /// Used by both sync and async connectors for CREATE VIEW operations.
        /// </summary>
        protected string BuildViewSelectSql(Tables.View view)
        {
            if (view.Join == null || !view.Join.Any())
            {
                throw new InvalidOperationException("View must have at least one join definition.");
            }

            var fields = view.GetSelectFields();
            if (fields == null || !fields.Any())
            {
                throw new InvalidOperationException("View must have at least one field.");
            }

            var tableFields = view.GetTableFields().ToArray();

            var sql = "SELECT " + string.Join(", ", fields.Select(f =>
            {
                var fieldAtIndex = f.Key < tableFields.Length ? tableFields[f.Key] : null;
                if (fieldAtIndex != null && fieldAtIndex.IsAggregate)
                {
                    return f.Value + " AS " + QuoteIdentifier(fieldAtIndex.Name);
                }
                return f.Value;
            }));

            sql += " FROM ";

            // Build JOINs — same logic as CreateSelectCommand in AbstractConnectorBase
            var joins = new Dictionary<string, List<Conditions.Join>>();
            string? prevleft = null;
            string? prevright = null;
            foreach (var join in view.Join)
            {
                if (!string.IsNullOrEmpty(prevleft) && !string.IsNullOrEmpty(prevright) && !joins.ContainsKey(join.Left) && prevright == join.Left && joins.ContainsKey(prevleft))
                {
                    joins[prevleft].Add(join);
                }
                else
                {
                    if (!joins.ContainsKey(join.Left))
                    {
                        joins.Add(join.Left, new List<Conditions.Join>());
                    }
                    joins[join.Left].Add(join);
                    prevleft = join.Left;
                }
                prevright = join.Right;
            }

            var leftTables = view.Join.Select(x => x.Left).Distinct().Where(x => !string.IsNullOrEmpty(x)).ToList();
            foreach (var tableName in view.Join.Select(x => x.Right).Distinct().Where(x => !string.IsNullOrEmpty(x)))
            {
                leftTables.Remove(tableName);
            }
            var tableNames = leftTables.Any() ? (IEnumerable<string>)leftTables : view.Tables.Select(x => x.Name);

            int i = 0;
            foreach (var table in tableNames.Distinct())
            {
                if (i > 0)
                {
                    sql += ", ";
                }
                sql += QuoteIdentifier(table);
                if (joins.ContainsKey(table))
                {
                    var joingroups = joins[table]
                        .GroupBy(x => new { x.Right, x.JoinType })
                        .ToDictionary(
                            x => x.Key,
                            x => x.SelectMany(y => y.Conditions ?? Enumerable.Empty<Conditions.Condition>()).Where(z => z != null));

                    foreach (var joingroup in joingroups.Where(x => x.Value.Any()))
                    {
                        sql += joingroup.Key.JoinType switch
                        {
                            Conditions.JoinType.Inner => " INNER JOIN ",
                            Conditions.JoinType.LeftOuter => " LEFT OUTER JOIN ",
                            _ => " CROSS JOIN ",
                        };
                        sql += QuoteIdentifier(joingroup.Key.Right);
                        if (joingroup.Key.JoinType != Conditions.JoinType.Cross && joingroup.Value != null && joingroup.Value.Any())
                        {
                            sql += " ON (";
                            sql += BuildViewJoinConditionSql(joingroup.Value);
                            sql += ")";
                        }
                    }
                }
                i++;
            }

            // GROUP BY for aggregate views
            if (view.HasAggregateFields())
            {
                var groupFields = view.GetSelectFields(true);
                if (groupFields != null && groupFields.Any())
                {
                    sql += " GROUP BY " + string.Join(", ", groupFields.Values);
                }
            }

            return sql;
        }

        /// <summary>
        /// Builds join condition SQL for view creation (field = field comparisons).
        /// </summary>
        private string BuildViewJoinConditionSql(IEnumerable<Conditions.Condition> conditions)
        {
            var parts = new List<string>();
            foreach (var condition in conditions)
            {
                if (condition.IsField && condition.Values != null)
                {
                    var fieldName = condition.Values.Cast<object>().FirstOrDefault()?.ToString();
                    if (!string.IsNullOrEmpty(condition.Name) && !string.IsNullOrEmpty(fieldName))
                    {
                        var left = QuoteFieldReference(condition.Name);
                        var right = QuoteFieldReference(fieldName);
                        parts.Add(left + " = " + right);
                    }
                }
                else if (!string.IsNullOrEmpty(condition.Name) && condition.Values != null)
                {
                    var value = condition.Values.Cast<object>().FirstOrDefault();
                    if (value != null)
                    {
                        var left = QuoteFieldReference(condition.Name);
                        parts.Add(left + " = '" + value.ToString()!.Replace("'", "''") + "'");
                    }
                }
            }
            return string.Join(" AND ", parts);
        }

        /// <summary>
        /// Quotes a dotted field reference (e.g., "TableName.FieldName" → "\"TableName\".\"FieldName\"").
        /// </summary>
        private string QuoteFieldReference(string fieldRef)
        {
            if (fieldRef.Contains('.'))
            {
                return string.Join(".", fieldRef.Split('.').Select(p => QuoteIdentifier(p)));
            }
            return QuoteIdentifier(fieldRef);
        }
    }
}
