using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;

namespace Birko.Data.SQL.Connectors
{
    public abstract partial class AbstractConnectorBase
    {
        /// <summary>
        /// Cache for persistent view existence checks. Maps view name to whether the view exists.
        /// Thread-safe for concurrent access.
        /// </summary>
        private readonly ConcurrentDictionary<string, bool> _viewExistsCache = new();

        /// <summary>
        /// Checks whether a persistent view should be used based on the view's QueryMode
        /// and (for Auto mode) the cached view existence status.
        /// </summary>
        /// <param name="view">The view metadata.</param>
        /// <param name="checkViewExists">Function to check if the view exists in the database (used only for Auto mode).</param>
        /// <returns>True if the persistent view should be queried directly; false for on-the-fly SELECT.</returns>
        protected bool ShouldUsePersistentView(Tables.View view, Func<string, bool> checkViewExists)
        {
            if (view.QueryMode == ViewQueryMode.OnTheFly)
            {
                return false;
            }

            if (view.QueryMode == ViewQueryMode.Persistent)
            {
                return true;
            }

            // Auto mode: check cache, then database
            var viewName = view.Name;
            if (string.IsNullOrEmpty(viewName))
            {
                return false;
            }

            return _viewExistsCache.GetOrAdd(viewName!, name => checkViewExists(name));
        }

        /// <summary>
        /// Creates a SELECT command targeting a persistent database VIEW (no joins needed).
        /// </summary>
        protected DbCommand CreatePersistentViewSelectCommand(
            DbCommand command,
            Tables.View view,
            IEnumerable<Conditions.Condition>? conditions = null,
            IDictionary<string, bool>? orderFields = null,
            int? limit = null,
            int? offset = null)
        {
            var viewName = view.Name;
            if (string.IsNullOrEmpty(viewName))
            {
                throw new InvalidOperationException("View name cannot be empty for persistent view query.");
            }

            var fields = view.GetPersistentViewSelectFields();
            if (fields == null || !fields.Any())
            {
                throw new InvalidOperationException("View must have at least one field.");
            }

            command.CommandText = "SELECT " + string.Join(", ", fields.Values.Select(f => QuoteIdentifier(f)))
                + " FROM " + QuoteIdentifier(viewName!);

            AddWhere(conditions, command);

            if (orderFields != null && orderFields.Any())
            {
                command.CommandText += " ORDER BY " + string.Join(", ", orderFields.Select(kvp =>
                    string.Format("{0} {1}", kvp.Key, kvp.Value ? "DESC" : "ASC")));
            }

            if (limit != null)
            {
                command.CommandText += LimitOffsetDefinition(command, limit, offset) ?? string.Empty;
            }

            return command;
        }

        /// <summary>
        /// Creates a SELECT COUNT(*) command targeting a persistent database VIEW.
        /// </summary>
        protected DbCommand CreatePersistentViewSelectCountCommand(
            DbCommand command,
            string viewName,
            IEnumerable<Conditions.Condition>? conditions = null)
        {
            if (string.IsNullOrEmpty(viewName))
            {
                throw new InvalidOperationException("View name cannot be empty for persistent view count query.");
            }

            command.CommandText = "SELECT COUNT(*) FROM " + QuoteIdentifier(viewName);

            AddWhere(conditions, command);

            return command;
        }

        /// <summary>
        /// Clears the cached view existence results. Call this after creating or dropping views
        /// to ensure Auto mode re-checks view existence.
        /// </summary>
        public void ClearViewExistsCache()
        {
            _viewExistsCache.Clear();
        }

        /// <summary>
        /// Removes a specific view name from the existence cache.
        /// </summary>
        public void InvalidateViewExistsCache(string viewName)
        {
            if (string.IsNullOrEmpty(viewName))
            {
                return;
            }

            _viewExistsCache.TryRemove(viewName, out _);
        }
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
