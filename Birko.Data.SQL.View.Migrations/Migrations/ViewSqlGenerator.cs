using Birko.Data.SQL;
using Birko.Data.SQL.Attributes;
using Birko.Data.SQL.Conditions;
using Birko.Data.SQL.Fields;
using Birko.Data.SQL.Tables;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Birko.Data.SQL.View.Migrations
{
    /// <summary>
    /// Static utility for generating view-related SQL statements from view type metadata.
    /// Used by migration extensions to create/drop views without requiring a connector instance.
    /// </summary>
    public static class ViewSqlGenerator
    {
        /// <summary>
        /// Default quote character for SQL identifiers (ANSI SQL double quote).
        /// </summary>
        public const char DefaultQuoteChar = '"';

        /// <summary>
        /// Generates a CREATE OR REPLACE VIEW SQL statement from a view type's metadata.
        /// </summary>
        /// <param name="viewType">The type decorated with ViewAttribute(s) and ViewFieldAttribute(s).</param>
        /// <param name="viewName">Optional custom view name. If null, derives from type metadata.</param>
        /// <param name="quoteChar">Quote character for identifiers. Defaults to ANSI SQL double quote.</param>
        /// <returns>The CREATE OR REPLACE VIEW SQL statement.</returns>
        public static string GenerateCreateViewSql(Type viewType, string? viewName = null, char quoteChar = DefaultQuoteChar)
        {
            if (viewType == null)
            {
                throw new ArgumentNullException(nameof(viewType));
            }

            var view = DataBase.LoadView(viewType);
            if (view == null || view.Tables == null || !view.Tables.Any())
            {
                throw new InvalidOperationException($"Type '{viewType.Name}' does not have valid view attributes.");
            }

            var name = viewName ?? GetViewName(viewType);
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new InvalidOperationException("View name cannot be empty. Provide a viewName parameter or set ViewAttribute.Name.");
            }

            var selectSql = BuildViewSelectSql(view, quoteChar);
            return "CREATE OR REPLACE VIEW " + QuoteIdentifier(name!, quoteChar) + " AS " + selectSql;
        }

        /// <summary>
        /// Generates a DROP VIEW IF EXISTS SQL statement from a view type's metadata.
        /// </summary>
        /// <param name="viewType">The type decorated with ViewAttribute(s).</param>
        /// <param name="viewName">Optional custom view name. If null, derives from type metadata.</param>
        /// <param name="quoteChar">Quote character for identifiers.</param>
        /// <returns>The DROP VIEW IF EXISTS SQL statement.</returns>
        public static string GenerateDropViewSql(Type viewType, string? viewName = null, char quoteChar = DefaultQuoteChar)
        {
            if (viewType == null)
            {
                throw new ArgumentNullException(nameof(viewType));
            }

            var name = viewName ?? GetViewName(viewType);
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new InvalidOperationException("View name cannot be empty. Provide a viewName parameter or set ViewAttribute.Name.");
            }

            return GenerateDropViewSql(name!, quoteChar);
        }

        /// <summary>
        /// Generates a DROP VIEW IF EXISTS SQL statement from a view name.
        /// </summary>
        /// <param name="viewName">The name of the view to drop.</param>
        /// <param name="quoteChar">Quote character for identifiers.</param>
        /// <returns>The DROP VIEW IF EXISTS SQL statement.</returns>
        public static string GenerateDropViewSql(string viewName, char quoteChar = DefaultQuoteChar)
        {
            if (string.IsNullOrWhiteSpace(viewName))
            {
                throw new ArgumentException("View name cannot be null or empty.", nameof(viewName));
            }

            return "DROP VIEW IF EXISTS " + QuoteIdentifier(viewName, quoteChar);
        }

        /// <summary>
        /// Extracts the view name from ViewAttribute metadata or defaults to the type name.
        /// Checks the first ViewAttribute's Name property; if null, concatenates the underlying table names.
        /// Falls back to the type name if no tables are resolved.
        /// </summary>
        /// <param name="viewType">The type decorated with ViewAttribute(s).</param>
        /// <returns>The resolved view name.</returns>
        public static string GetViewName(Type viewType)
        {
            if (viewType == null)
            {
                throw new ArgumentNullException(nameof(viewType));
            }

            // Check ViewAttribute.Name first
            var attrs = viewType.GetCustomAttributes(typeof(ViewAttribute), true)
                .OfType<ViewAttribute>()
                .ToArray();

            if (attrs.Length > 0)
            {
                var attrName = attrs[0].Name;
                if (!string.IsNullOrWhiteSpace(attrName))
                {
                    return attrName;
                }
            }

            // Try to derive from the loaded view's table names
            var view = DataBase.LoadView(viewType);
            if (view?.Tables != null && view.Tables.Any())
            {
                var concatenated = string.Join(string.Empty,
                    view.Tables.Select(x => x.Name).Where(x => !string.IsNullOrEmpty(x)).Distinct());
                if (!string.IsNullOrEmpty(concatenated))
                {
                    return concatenated;
                }
            }

            // Fall back to type name
            return viewType.Name;
        }

        /// <summary>
        /// Quotes a SQL identifier using the specified quote character.
        /// </summary>
        private static string QuoteIdentifier(string identifier, char quoteChar)
        {
            var quoteStr = quoteChar.ToString();
            return quoteStr + identifier.Replace(quoteStr, quoteStr + quoteStr) + quoteStr;
        }

        /// <summary>
        /// Quotes a dotted field reference (e.g., "TableName.FieldName").
        /// </summary>
        private static string QuoteFieldReference(string fieldRef, char quoteChar)
        {
            if (fieldRef.Contains('.'))
            {
                return string.Join(".", fieldRef.Split('.').Select(p => QuoteIdentifier(p, quoteChar)));
            }
            return QuoteIdentifier(fieldRef, quoteChar);
        }

        /// <summary>
        /// Builds the SELECT SQL that defines a view's body from view metadata.
        /// Replicates the logic from AbstractConnectorBase.BuildViewSelectSql() in a static context.
        /// </summary>
        private static string BuildViewSelectSql(Tables.View view, char quoteChar)
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
                    return f.Value + " AS " + QuoteIdentifier(fieldAtIndex.Name, quoteChar);
                }
                return f.Value;
            }));

            sql += " FROM ";

            // Build JOINs — same logic as AbstractConnectorBase.BuildViewSelectSql
            var joins = new Dictionary<string, List<Conditions.Join>>();
            string? prevleft = null;
            string? prevright = null;
            foreach (var join in view.Join)
            {
                if (!string.IsNullOrEmpty(prevleft) && !string.IsNullOrEmpty(prevright)
                    && !joins.ContainsKey(join.Left) && prevright == join.Left
                    && joins.ContainsKey(prevleft))
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
            var tableNames = leftTables.Any()
                ? (IEnumerable<string>)leftTables
                : view.Tables.Select(x => x.Name);

            int i = 0;
            foreach (var table in tableNames.Distinct())
            {
                if (i > 0)
                {
                    sql += ", ";
                }
                sql += QuoteIdentifier(table, quoteChar);
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
                        sql += QuoteIdentifier(joingroup.Key.Right, quoteChar);
                        if (joingroup.Key.JoinType != Conditions.JoinType.Cross && joingroup.Value != null && joingroup.Value.Any())
                        {
                            sql += " ON (";
                            sql += BuildViewJoinConditionSql(joingroup.Value, quoteChar);
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
        private static string BuildViewJoinConditionSql(IEnumerable<Conditions.Condition> conditions, char quoteChar)
        {
            var parts = new List<string>();
            foreach (var condition in conditions)
            {
                if (condition.IsField && condition.Values != null)
                {
                    var fieldName = condition.Values.Cast<object>().FirstOrDefault()?.ToString();
                    if (!string.IsNullOrEmpty(condition.Name) && !string.IsNullOrEmpty(fieldName))
                    {
                        var left = QuoteFieldReference(condition.Name, quoteChar);
                        var right = QuoteFieldReference(fieldName, quoteChar);
                        parts.Add(left + " = " + right);
                    }
                }
                else if (!string.IsNullOrEmpty(condition.Name) && condition.Values != null)
                {
                    var value = condition.Values.Cast<object>().FirstOrDefault();
                    if (value != null)
                    {
                        var left = QuoteFieldReference(condition.Name, quoteChar);
                        parts.Add(left + " = '" + value.ToString()!.Replace("'", "''") + "'");
                    }
                }
            }
            return string.Join(" AND ", parts);
        }
    }
}
