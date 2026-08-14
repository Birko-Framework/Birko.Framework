using System;
using System.Collections.Generic;
using System.Linq;

namespace Birko.Data.SQL.Connectors
{
    /// <summary>
    /// Shared builder for the SELECT SQL that defines a view's body (no WHERE, ORDER BY, LIMIT, OFFSET).
    /// Extracted from <c>AbstractConnectorBase.BuildViewSelectSql</c> so the base connector, the
    /// provider-specific indexed/materialized-view builders (SQL Server SCHEMABINDING) and the migration
    /// SQL generator share a single implementation (CR-M140 / CR-M151) instead of maintaining three
    /// copies of the intricate join-grouping + aggregate-<c>AS</c> logic that would silently drift.
    /// The only variation between callers is how a table name is emitted in the FROM/JOIN clause, which
    /// is parameterized via <paramref name="qualifyTableName"/>.
    /// </summary>
    public static class ViewSelectSqlBuilder
    {
        /// <summary>
        /// Builds the SELECT SQL that defines a view's body.
        /// </summary>
        /// <param name="view">The view metadata.</param>
        /// <param name="quoteIdentifier">
        /// Quotes a single SQL identifier (column names in the SELECT list, aggregate <c>AS</c> aliases,
        /// and each segment of a dotted field reference in a join condition).
        /// </param>
        /// <param name="qualifyTableName">
        /// Produces the table-name token emitted in the FROM/JOIN clause. Defaults to
        /// <paramref name="quoteIdentifier"/> (plain quoted name); SQL Server's SCHEMABINDING path
        /// overrides it to emit a two-part <c>[dbo].[Table]</c> name.
        /// </param>
        public static string BuildViewSelectSql(
            Tables.View view,
            Func<string, string> quoteIdentifier,
            Func<string, string>? qualifyTableName = null)
        {
            if (view == null)
            {
                throw new ArgumentNullException(nameof(view));
            }
            if (quoteIdentifier == null)
            {
                throw new ArgumentNullException(nameof(quoteIdentifier));
            }
            qualifyTableName ??= quoteIdentifier;

            if (view.Join == null || !view.Join.Any())
            {
                throw new InvalidOperationException("View must have at least one join definition.");
            }

            // TASK-129: request the projection WITHOUT its aggregate alias, because this method appends its
            // own. Both emitting produced `COUNT(VOrders.PersonId) as COUNT AS "OrderCount"` — two aliases
            // on one column, which SQLite rejects with `near "AS": syntax error` and which is a syntax error
            // on every other provider, so no persistent (or Auto) aggregate view could be created at all.
            var fields = view.GetSelectFields(aggregateAlias: false, quoteTable: quoteIdentifier);
            if (fields == null || !fields.Any())
            {
                throw new InvalidOperationException("View must have at least one field.");
            }

            var tableFields = view.GetTableFields().ToArray();

            var sql = "SELECT " + string.Join(", ", fields.Select(f =>
            {
                var fieldAtIndex = f.Key < tableFields.Length ? tableFields[f.Key] : null;
                if (fieldAtIndex?.Property != null)
                {
                    // TASK-209: alias EVERY column by its view property, and BARE.
                    //
                    // Every column, because a non-aggregate previously carried no alias at all and so was
                    // created under its source column name — which is wrong twice over: the persistent read
                    // asks for the view property, and two view properties over one source column produced
                    // one duplicated output name (TASK-207).
                    //
                    // Bare, because the whole point is that the alias must round-trip against a reader that
                    // is now also bare. TASK-129 quoted this alias to agree with a reader that quoted; that
                    // pairing was self-consistent but broke against the rest of the framework, whose
                    // base-table DDL emits column definitions bare so that every base column folds on
                    // PostgreSQL. Measured against PostgreSQL 16.4: the DDL below creates `name` /
                    // `ordercount`, and a bare `SELECT Name, OrderCount` resolves while the previously
                    // shipped `SELECT "Name", "OrderCount"` fails with `column "Name" does not exist`.
                    // The persistent ORDER BY already interpolated its key bare, so it becomes the model the
                    // other two producers were brought into line with, not the outlier.
                    return f.Value + " AS " + fieldAtIndex.Property.Name;
                }
                return f.Value;
            }));

            sql += " FROM ";

            // Build JOINs — group consecutive joins that chain off the same left table.
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
                sql += qualifyTableName(table);
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
                        sql += qualifyTableName(joingroup.Key.Right);
                        if (joingroup.Key.JoinType != Conditions.JoinType.Cross && joingroup.Value != null && joingroup.Value.Any())
                        {
                            sql += " ON (";
                            sql += BuildViewJoinConditionSql(joingroup.Value, quoteIdentifier);
                            sql += ")";
                        }
                    }
                }
                i++;
            }

            // GROUP BY for aggregate views
            if (view.HasAggregateFields())
            {
                var groupFields = view.GetSelectFields(true, quoteTable: quoteIdentifier);
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
        private static string BuildViewJoinConditionSql(IEnumerable<Conditions.Condition> conditions, Func<string, string> quoteIdentifier)
        {
            var parts = new List<string>();
            foreach (var condition in conditions)
            {
                if (condition.IsField && condition.Values != null)
                {
                    var fieldName = condition.Values.Cast<object>().FirstOrDefault()?.ToString();
                    if (!string.IsNullOrEmpty(condition.Name) && !string.IsNullOrEmpty(fieldName))
                    {
                        var left = QuoteFieldReference(condition.Name, quoteIdentifier);
                        var right = QuoteFieldReference(fieldName, quoteIdentifier);
                        parts.Add(left + " = " + right);
                    }
                }
                else if (!string.IsNullOrEmpty(condition.Name) && condition.Values != null)
                {
                    var value = condition.Values.Cast<object>().FirstOrDefault();
                    if (value != null)
                    {
                        var left = QuoteFieldReference(condition.Name, quoteIdentifier);
                        parts.Add(left + " = " + FormatJoinConditionValue(value));
                    }
                }
            }
            return string.Join(" AND ", parts);
        }

        /// <summary>
        /// Formats a constant join-condition value as a SQL literal. Numerics and bools are emitted
        /// unquoted (numerics via InvariantCulture so a comma-decimal locale doesn't corrupt the SQL);
        /// everything else is a single-quoted string with embedded quotes doubled (CR-L196). CREATE VIEW
        /// cannot be parameterized, so view-definition constants must be trusted (not user input).
        /// </summary>
        internal static string FormatJoinConditionValue(object value)
        {
            return value switch
            {
                bool b => b ? "TRUE" : "FALSE",
                byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal
                    => System.Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture)!,
                _ => "'" + value.ToString()!.Replace("'", "''") + "'",
            };
        }

        /// <summary>
        /// Quotes a dotted field reference (e.g., "TableName.FieldName" → quoted each segment).
        /// </summary>
        /// <summary>
        /// Emits a join-condition field reference: the <b>table</b> half quoted, the <b>column</b> half bare.
        /// <para>
        /// TASK-209. This quoted both halves, so a join emitted <c>"AvOrders"."PersonId"</c> while the base
        /// table — whose DDL writes column definitions bare — actually holds <c>personid</c> on PostgreSQL.
        /// Measured: <c>ERROR: column AvOrders.PersonId does not exist</c>, which stopped the view being
        /// created one step after the qualifier defect and two steps before the one that was filed.
        /// </para>
        /// </summary>
        private static string QuoteFieldReference(string fieldRef, Func<string, string> quoteIdentifier)
        {
            var separator = fieldRef.IndexOf('.');
            if (separator > 0)
            {
                return quoteIdentifier(fieldRef.Substring(0, separator)) + "." + fieldRef.Substring(separator + 1);
            }
            return fieldRef;
        }
    }
}
