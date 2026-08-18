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
        /// Gets the schema name used for SCHEMABINDING two-part names. Defaults to "dbo"; override in a
        /// derived connector for databases whose target tables live in a non-dbo schema (CR-L180).
        /// </summary>
        protected virtual string GetSchemaName()
        {
            return "dbo";
        }

        /// <summary>
        /// SQL Server requires an aggregate indexed view (WITH SCHEMABINDING + GROUP BY) to include
        /// COUNT_BIG(*) in its select list, which the generic aggregate SELECT builder does not emit —
        /// so the unique clustered index (CreateIndexedView step 2) would fail at runtime with an opaque
        /// error. Fail fast with a clear message instead (CR-L181). Non-aggregate indexed views are
        /// unaffected.
        /// </summary>
        private static void EnsureIndexedViewSupported(Tables.View view)
        {
            if (view.HasAggregateFields())
            {
                throw new System.NotSupportedException(
                    "Aggregate (GROUP BY) indexed views are not supported by CreateIndexedView: SQL Server " +
                    "requires COUNT_BIG(*) in the select list of a SCHEMABINDING aggregate view, which the " +
                    "generated SELECT does not include. Use a non-aggregate view, or create the aggregate " +
                    "indexed view manually with an explicit COUNT_BIG(*) column.");
            }
        }

        /// <summary>
        /// Builds a SELECT SQL for an indexed view using two-part table names (schema.table)
        /// required by SCHEMABINDING. Delegates to the shared <see cref="ViewSelectSqlBuilder"/>
        /// (CR-M140) passing a table-qualifier that emits the two-part <c>[dbo].[Table]</c> name;
        /// the field/aggregate/join-grouping logic is identical to the base connector's regular views.
        /// The qualifier is scoped to this SCHEMABINDING path so regular (non-indexed) MSSql views
        /// keep their plain single-part names.
        /// </summary>
        private string BuildSchemaBindingSelectSql(Tables.View view)
        {
            var schema = GetSchemaName();
            return ViewSelectSqlBuilder.BuildViewSelectSql(
                view,
                QuoteIdentifier,
                table => QuoteIdentifier(schema) + "." + QuoteIdentifier(table));
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

            EnsureIndexedViewSupported(view);
            var selectSql = BuildSchemaBindingSelectSql(view);
            var keyColumns = GetIndexedViewKeyColumns(view);
            var indexName = "IX_" + name;

            // Step 1: Create the view with SCHEMABINDING
            DoDdlCommand((command) =>
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

            DoDdlCommand((command) =>
            {
                command.CommandText = "CREATE UNIQUE CLUSTERED INDEX " + QuoteIdentifier(indexName!) + " ON " + QuoteIdentifier(name!) + " (" + columnsSql + ")";
            }, (command) =>
            {
                command.ExecuteNonQuery();
            }, true);

            // Invalidate the shared view-existence cache so Auto-mode re-checks and starts using the
            // now-created view, mirroring the base CreateView/DropView (CR-H090).
            InvalidateViewExistsCache(name!);
        }

        /// <summary>
        /// Asynchronously creates an indexed view in SQL Server with SCHEMABINDING and a unique clustered index.
        /// </summary>
        /// <param name="viewType">The type decorated with ViewAttribute(s).</param>
        /// <param name="viewName">Optional custom view name.</param>
        /// <param name="ct">Cancellation token.</param>
        public async Task CreateIndexedViewAsync(System.Type viewType, string? viewName = null, CancellationToken ct = default)
        {
            // CR-M139: genuine async via the connector's async primitives instead of Task.Run(sync),
            // so the CancellationToken is observed during the DB round-trips and no thread-pool thread
            // is blocked on ADO.NET.
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

            EnsureIndexedViewSupported(view);
            var selectSql = BuildSchemaBindingSelectSql(view);
            var keyColumns = GetIndexedViewKeyColumns(view);
            var indexName = "IX_" + name;

            // Step 1: Create the view with SCHEMABINDING
            await DoDdlCommandAsync(async (command) =>
            {
                command.CommandText = "CREATE OR ALTER VIEW " + QuoteIdentifier(name!) + " WITH SCHEMABINDING AS " + selectSql;
                await System.Threading.Tasks.Task.CompletedTask;
            }, async (command) =>
            {
                await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }, true, ct).ConfigureAwait(false);

            // Step 2: Create the unique clustered index
            var columnsSql = string.Join(", ", keyColumns.Select(c =>
            {
                if (c.Contains('.'))
                {
                    return string.Join(".", c.Split('.').Select(p => QuoteIdentifier(p)));
                }
                return QuoteIdentifier(c);
            }));

            await DoDdlCommandAsync(async (command) =>
            {
                command.CommandText = "CREATE UNIQUE CLUSTERED INDEX " + QuoteIdentifier(indexName!) + " ON " + QuoteIdentifier(name!) + " (" + columnsSql + ")";
                await System.Threading.Tasks.Task.CompletedTask;
            }, async (command) =>
            {
                await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }, true, ct).ConfigureAwait(false);

            InvalidateViewExistsCache(name!);
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

            DoDdlCommand((command) =>
            {
                command.CommandText = "DROP VIEW IF EXISTS " + QuoteIdentifier(viewName);
            }, (command) =>
            {
                command.ExecuteNonQuery();
            }, true);

            // Invalidate the cached 'exists' so Auto-mode stops targeting the dropped view (CR-H090).
            InvalidateViewExistsCache(viewName);
        }

        /// <summary>
        /// Asynchronously drops an indexed view in SQL Server.
        /// </summary>
        /// <param name="viewName">The name of the indexed view to drop.</param>
        /// <param name="ct">Cancellation token.</param>
        public async Task DropIndexedViewAsync(string viewName, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(viewName))
                throw new System.ArgumentException("View name cannot be null or empty.", nameof(viewName));

            // CR-M139: genuine async instead of Task.Run(sync).
            await DoDdlCommandAsync(async (command) =>
            {
                command.CommandText = "DROP VIEW IF EXISTS " + QuoteIdentifier(viewName);
                await System.Threading.Tasks.Task.CompletedTask;
            }, async (command) =>
            {
                await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }, true, ct).ConfigureAwait(false);

            InvalidateViewExistsCache(viewName);
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
        public async Task<bool> IndexedViewExistsAsync(string viewName, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(viewName))
                throw new System.ArgumentException("View name cannot be null or empty.", nameof(viewName));

            // CR-M139: genuine async via DoCommandAsync + ExecuteReaderAsync instead of Task.Run(sync).
            bool exists = false;
            await DoCommandAsync(async (command) =>
            {
                command.CommandText = @"SELECT 1 FROM sys.views v
INNER JOIN sys.indexes i ON v.object_id = i.object_id
WHERE v.name = @viewName AND v.is_ms_shipped = 0 AND i.type = 1";
                var param = command.CreateParameter();
                param.ParameterName = "@viewName";
                param.Value = viewName;
                command.Parameters.Add(param);
                await System.Threading.Tasks.Task.CompletedTask;
            }, async (command) =>
            {
                using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
                exists = reader.HasRows;
            }, false, ct).ConfigureAwait(false);
            return exists;
        }
    }
}
