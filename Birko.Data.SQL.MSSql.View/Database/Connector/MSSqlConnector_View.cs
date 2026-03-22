using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Birko.Data.SQL.Connectors
{
    public partial class MSSqlConnector
    {
        /// <summary>
        /// Builds the CREATE VIEW SQL for Microsoft SQL Server.
        /// Uses CREATE OR ALTER VIEW (requires SQL Server 2016 SP1+).
        /// </summary>
        protected override string BuildCreateViewSql(string viewName, string selectSql)
        {
            return "CREATE OR ALTER VIEW " + QuoteIdentifier(viewName) + " AS " + selectSql;
        }

        /// <summary>
        /// Checks if a view exists in SQL Server using sys.views catalog.
        /// </summary>
        public override bool ViewExists(string viewName)
        {
            if (string.IsNullOrWhiteSpace(viewName))
                throw new System.ArgumentException("View name cannot be null or empty.", nameof(viewName));

            bool exists = false;
            DoCommand((command) =>
            {
                command.CommandText = "SELECT 1 FROM sys.views WHERE name = @viewName";
                var param = command.CreateParameter();
                param.ParameterName = "@viewName";
                param.Value = viewName;
                command.Parameters.Add(param);
            }, (command) =>
            {
                using var reader = command.ExecuteReader();
                exists = reader.HasRows;
            });
            return exists;
        }

        /// <summary>
        /// Gets the schema name for SCHEMABINDING. Defaults to "dbo".
        /// </summary>
        private string GetSchemaName()
        {
            return "dbo";
        }

        /// <summary>
        /// Builds a SELECT SQL for an indexed view using two-part table names (schema.table)
        /// required by SCHEMABINDING.
        /// </summary>
        private string BuildSchemaBindingSelectSql(Tables.View view)
        {
            if (view.Join == null || !view.Join.Any())
            {
                throw new System.InvalidOperationException("View must have at least one join definition.");
            }

            var fields = view.GetSelectFields();
            if (fields == null || !fields.Any())
            {
                throw new System.InvalidOperationException("View must have at least one field.");
            }

            var tableFields = view.GetTableFields().ToArray();
            var schema = GetSchemaName();

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
            var tableNames = leftTables.Any() ? (System.Collections.Generic.IEnumerable<string>)leftTables : view.Tables.Select(x => x.Name);

            int i = 0;
            foreach (var table in tableNames.Distinct())
            {
                if (i > 0)
                {
                    sql += ", ";
                }
                // Two-part name: [dbo].[TableName]
                sql += QuoteIdentifier(schema) + "." + QuoteIdentifier(table);
                if (joins.ContainsKey(table))
                {
                    var joingroups = joins[table]
                        .GroupBy(x => new { x.Right, x.JoinType })
                        .ToDictionary(
                            x => x.Key,
                            x => x.SelectMany(y => y.Conditions ?? System.Linq.Enumerable.Empty<Conditions.Condition>()).Where(z => z != null));

                    foreach (var joingroup in joingroups.Where(x => x.Value.Any()))
                    {
                        sql += joingroup.Key.JoinType switch
                        {
                            Conditions.JoinType.Inner => " INNER JOIN ",
                            Conditions.JoinType.LeftOuter => " LEFT OUTER JOIN ",
                            _ => " CROSS JOIN ",
                        };
                        // Two-part name for joined tables as well
                        sql += QuoteIdentifier(schema) + "." + QuoteIdentifier(joingroup.Key.Right);
                        if (joingroup.Key.JoinType != Conditions.JoinType.Cross && joingroup.Value != null && joingroup.Value.Any())
                        {
                            sql += " ON (";
                            sql += BuildSchemaBindingJoinConditionSql(joingroup.Value);
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
        /// Builds join condition SQL for indexed view creation (field = field comparisons).
        /// </summary>
        private string BuildSchemaBindingJoinConditionSql(System.Collections.Generic.IEnumerable<Conditions.Condition> conditions)
        {
            var parts = new List<string>();
            foreach (var condition in conditions)
            {
                if (condition.IsField && condition.Values != null)
                {
                    var fieldName = condition.Values.Cast<object>().FirstOrDefault()?.ToString();
                    if (!string.IsNullOrEmpty(condition.Name) && !string.IsNullOrEmpty(fieldName))
                    {
                        var left = QuoteSchemaFieldReference(condition.Name);
                        var right = QuoteSchemaFieldReference(fieldName);
                        parts.Add(left + " = " + right);
                    }
                }
                else if (!string.IsNullOrEmpty(condition.Name) && condition.Values != null)
                {
                    var value = condition.Values.Cast<object>().FirstOrDefault();
                    if (value != null)
                    {
                        var left = QuoteSchemaFieldReference(condition.Name);
                        parts.Add(left + " = '" + value.ToString()!.Replace("'", "''") + "'");
                    }
                }
            }
            return string.Join(" AND ", parts);
        }

        /// <summary>
        /// Quotes a dotted field reference (e.g., "TableName.FieldName" -> "[TableName].[FieldName]").
        /// </summary>
        private string QuoteSchemaFieldReference(string fieldRef)
        {
            if (fieldRef.Contains('.'))
            {
                return string.Join(".", fieldRef.Split('.').Select(p => QuoteIdentifier(p)));
            }
            return QuoteIdentifier(fieldRef);
        }

        /// <summary>
        /// Gets the columns for the clustered index from the view's primary key or first unique fields.
        /// Falls back to the first field if no primary/unique fields are found.
        /// </summary>
        private IEnumerable<string> GetIndexedViewKeyColumns(Tables.View view)
        {
            // Try primary key fields first across all tables
            foreach (var table in view.Tables)
            {
                var primaryFields = table.GetPrimaryFields();
                if (primaryFields != null && primaryFields.Any())
                {
                    return primaryFields.Select(f => f.Table.Name + "." + f.Name);
                }
            }

            // Fall back to unique fields
            foreach (var table in view.Tables)
            {
                var uniqueFields = table.Fields?.Values.Where(f => f.IsUnique);
                if (uniqueFields != null && uniqueFields.Any())
                {
                    return uniqueFields.Select(f => f.Table.Name + "." + f.Name);
                }
            }

            // Last resort: use the first field from the first table
            var firstTable = view.Tables.First();
            var firstField = firstTable.Fields?.Values.FirstOrDefault();
            if (firstField != null)
            {
                return new[] { firstTable.Name + "." + firstField.Name };
            }

            throw new System.InvalidOperationException("Cannot determine index columns for indexed view. View must have at least one field.");
        }

        /// <summary>
        /// Creates an indexed view in SQL Server with SCHEMABINDING and a unique clustered index.
        /// Indexed views persist computed results and are automatically maintained by the engine.
        /// </summary>
        /// <param name="viewType">The type decorated with ViewAttribute(s).</param>
        /// <param name="viewName">Optional custom view name.</param>
        public void CreateIndexedView(System.Type viewType, string? viewName = null)
        {
            var view = DataBase.LoadView(viewType);
            if (view == null || view.Tables == null || !view.Tables.Any())
            {
                throw new System.InvalidOperationException($"Type '{viewType.Name}' does not have valid view attributes.");
            }

            var name = viewName ?? view.Name;
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new System.InvalidOperationException("View name cannot be empty.");
            }

            var selectSql = BuildSchemaBindingSelectSql(view);
            var keyColumns = GetIndexedViewKeyColumns(view);
            var indexName = "IX_" + name;

            // Step 1: Create the view with SCHEMABINDING
            DoCommandWithTransaction((command) =>
            {
                command.CommandText = "CREATE OR ALTER VIEW " + QuoteIdentifier(name!) + " WITH SCHEMABINDING AS " + selectSql;
            }, (command) =>
            {
                command.ExecuteNonQuery();
            }, true);

            // Step 2: Create the unique clustered index
            var columnsSql = string.Join(", ", keyColumns.Select(c =>
            {
                if (c.Contains('.'))
                {
                    return string.Join(".", c.Split('.').Select(p => QuoteIdentifier(p)));
                }
                return QuoteIdentifier(c);
            }));

            DoCommandWithTransaction((command) =>
            {
                command.CommandText = "CREATE UNIQUE CLUSTERED INDEX " + QuoteIdentifier(indexName!) + " ON " + QuoteIdentifier(name!) + " (" + columnsSql + ")";
            }, (command) =>
            {
                command.ExecuteNonQuery();
            }, true);
        }

        /// <summary>
        /// Asynchronously creates an indexed view in SQL Server with SCHEMABINDING and a unique clustered index.
        /// </summary>
        /// <param name="viewType">The type decorated with ViewAttribute(s).</param>
        /// <param name="viewName">Optional custom view name.</param>
        /// <param name="ct">Cancellation token.</param>
        public Task CreateIndexedViewAsync(System.Type viewType, string? viewName = null, CancellationToken ct = default)
        {
            return Task.Run(() => CreateIndexedView(viewType, viewName), ct);
        }

        /// <summary>
        /// Drops an indexed view in SQL Server.
        /// DROP VIEW handles indexed views (the clustered index is dropped automatically).
        /// </summary>
        /// <param name="viewName">The name of the indexed view to drop.</param>
        public void DropIndexedView(string viewName)
        {
            if (string.IsNullOrWhiteSpace(viewName))
                throw new System.ArgumentException("View name cannot be null or empty.", nameof(viewName));

            DoCommandWithTransaction((command) =>
            {
                command.CommandText = "DROP VIEW IF EXISTS " + QuoteIdentifier(viewName);
            }, (command) =>
            {
                command.ExecuteNonQuery();
            }, true);
        }

        /// <summary>
        /// Asynchronously drops an indexed view in SQL Server.
        /// </summary>
        /// <param name="viewName">The name of the indexed view to drop.</param>
        /// <param name="ct">Cancellation token.</param>
        public Task DropIndexedViewAsync(string viewName, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(viewName))
                throw new System.ArgumentException("View name cannot be null or empty.", nameof(viewName));

            return Task.Run(() => DropIndexedView(viewName), ct);
        }

        /// <summary>
        /// Checks if an indexed view exists in SQL Server.
        /// Verifies both the view existence in sys.views and the presence of a clustered index in sys.indexes.
        /// </summary>
        /// <param name="viewName">The name of the indexed view to check.</param>
        public bool IndexedViewExists(string viewName)
        {
            if (string.IsNullOrWhiteSpace(viewName))
                throw new System.ArgumentException("View name cannot be null or empty.", nameof(viewName));

            bool exists = false;
            DoCommand((command) =>
            {
                command.CommandText = @"SELECT 1 FROM sys.views v
INNER JOIN sys.indexes i ON v.object_id = i.object_id
WHERE v.name = @viewName AND v.is_ms_shipped = 0 AND i.type = 1";
                var param = command.CreateParameter();
                param.ParameterName = "@viewName";
                param.Value = viewName;
                command.Parameters.Add(param);
            }, (command) =>
            {
                using var reader = command.ExecuteReader();
                exists = reader.HasRows;
            });
            return exists;
        }

        /// <summary>
        /// Asynchronously checks if an indexed view exists in SQL Server.
        /// </summary>
        /// <param name="viewName">The name of the indexed view to check.</param>
        /// <param name="ct">Cancellation token.</param>
        public Task<bool> IndexedViewExistsAsync(string viewName, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(viewName))
                throw new System.ArgumentException("View name cannot be null or empty.", nameof(viewName));

            return Task.Run(() => IndexedViewExists(viewName), ct);
        }
    }
}
