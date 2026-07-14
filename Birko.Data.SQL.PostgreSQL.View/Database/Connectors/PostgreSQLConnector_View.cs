using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Birko.Data.SQL.Connectors
{
    public partial class PostgreSQLConnector
    {
        // PostgreSQL natively supports CREATE OR REPLACE VIEW (the default in AbstractConnectorBase).
        // No override needed for BuildCreateViewSql.

        // CR-M143: DDL composition extracted into these helpers so the SQL text (and identifier
        // quoting) is unit-testable without a live PostgreSQL, and shared by the sync + async paths.
        internal string BuildCreateMaterializedViewSql(string viewName, string selectSql)
            => "CREATE MATERIALIZED VIEW IF NOT EXISTS " + QuoteIdentifier(viewName) + " AS " + selectSql;

        internal string BuildRefreshMaterializedViewSql(string viewName, bool concurrently)
            => concurrently
                ? "REFRESH MATERIALIZED VIEW CONCURRENTLY " + QuoteIdentifier(viewName)
                : "REFRESH MATERIALIZED VIEW " + QuoteIdentifier(viewName);

        internal string BuildDropMaterializedViewSql(string viewName)
            => "DROP MATERIALIZED VIEW IF EXISTS " + QuoteIdentifier(viewName);

        /// <summary>
        /// Checks if a view exists in PostgreSQL using information_schema.
        /// </summary>
        public override bool ViewExists(string viewName)
        {
            if (string.IsNullOrWhiteSpace(viewName))
                throw new System.ArgumentException("View name cannot be null or empty.", nameof(viewName));

            bool exists = false;
            DoCommand((command) =>
            {
                // Scope to the current schema (where a bare, single-part CREATE VIEW lands) so a
                // same-named view in another schema on the search path isn't a false positive (CR-L191).
                command.CommandText = "SELECT 1 FROM information_schema.views WHERE table_name = @viewName AND table_schema = current_schema()";
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
        /// Checks if a materialized view exists in PostgreSQL using pg_matviews.
        /// </summary>
        public bool MaterializedViewExists(string viewName)
        {
            if (string.IsNullOrWhiteSpace(viewName))
                throw new System.ArgumentException("View name cannot be null or empty.", nameof(viewName));

            bool exists = false;
            DoCommand((command) =>
            {
                command.CommandText = "SELECT 1 FROM pg_matviews WHERE matviewname = @viewName AND schemaname = current_schema()";
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
        /// Creates a materialized view in PostgreSQL.
        /// Materialized views store query results physically and must be refreshed manually.
        /// </summary>
        /// <param name="viewType">The type decorated with ViewAttribute(s).</param>
        /// <param name="viewName">Optional custom view name.</param>
        public void CreateMaterializedView(System.Type viewType, string? viewName = null)
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

            var selectSql = BuildViewSelectSql(view);

            DoCommandWithTransaction((command) =>
            {
                command.CommandText = BuildCreateMaterializedViewSql(name!, selectSql);
            }, (command) =>
            {
                command.ExecuteNonQuery();
            }, true);
        }

        /// <summary>
        /// Refreshes a materialized view in PostgreSQL.
        /// </summary>
        /// <param name="viewName">The name of the materialized view to refresh.</param>
        /// <param name="concurrently">If true, refreshes without locking the view for reads (requires a unique index).</param>
        public void RefreshMaterializedView(string viewName, bool concurrently = false)
        {
            if (string.IsNullOrWhiteSpace(viewName))
                throw new System.ArgumentException("View name cannot be null or empty.", nameof(viewName));

            DoCommandWithTransaction((command) =>
            {
                command.CommandText = BuildRefreshMaterializedViewSql(viewName, concurrently);
            }, (command) =>
            {
                command.ExecuteNonQuery();
            }, true);
        }

        /// <summary>
        /// Drops a materialized view in PostgreSQL.
        /// </summary>
        public void DropMaterializedView(string viewName)
        {
            if (string.IsNullOrWhiteSpace(viewName))
                throw new System.ArgumentException("View name cannot be null or empty.", nameof(viewName));

            DoCommandWithTransaction((command) =>
            {
                command.CommandText = BuildDropMaterializedViewSql(viewName);
            }, (command) =>
            {
                command.ExecuteNonQuery();
            }, true);
        }

        /// <summary>
        /// Asynchronously creates a materialized view in PostgreSQL.
        /// Materialized views store query results physically and must be refreshed manually.
        /// </summary>
        /// <param name="viewType">The type decorated with ViewAttribute(s).</param>
        /// <param name="viewName">Optional custom view name.</param>
        /// <param name="ct">Cancellation token.</param>
        public Task CreateMaterializedViewAsync(System.Type viewType, string? viewName = null, CancellationToken ct = default)
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

            var selectSql = BuildViewSelectSql(view);

            return Task.Run(() =>
            {
                DoCommandWithTransaction((command) =>
                {
                    command.CommandText = BuildCreateMaterializedViewSql(name!, selectSql);
                }, (command) =>
                {
                    command.ExecuteNonQuery();
                }, true);
            }, ct);
        }

        /// <summary>
        /// Asynchronously refreshes a materialized view in PostgreSQL.
        /// </summary>
        /// <param name="viewName">The name of the materialized view to refresh.</param>
        /// <param name="concurrently">If true, refreshes without locking the view for reads (requires a unique index).</param>
        /// <param name="ct">Cancellation token.</param>
        public Task RefreshMaterializedViewAsync(string viewName, bool concurrently = false, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(viewName))
                throw new System.ArgumentException("View name cannot be null or empty.", nameof(viewName));

            return Task.Run(() =>
            {
                DoCommandWithTransaction((command) =>
                {
                    command.CommandText = concurrently
                        ? "REFRESH MATERIALIZED VIEW CONCURRENTLY " + QuoteIdentifier(viewName)
                        : "REFRESH MATERIALIZED VIEW " + QuoteIdentifier(viewName);
                }, (command) =>
                {
                    command.ExecuteNonQuery();
                }, true);
            }, ct);
        }

        /// <summary>
        /// Asynchronously drops a materialized view in PostgreSQL.
        /// </summary>
        /// <param name="viewName">The name of the materialized view to drop.</param>
        /// <param name="ct">Cancellation token.</param>
        public Task DropMaterializedViewAsync(string viewName, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(viewName))
                throw new System.ArgumentException("View name cannot be null or empty.", nameof(viewName));

            return Task.Run(() =>
            {
                DoCommandWithTransaction((command) =>
                {
                    command.CommandText = BuildDropMaterializedViewSql(viewName);
                }, (command) =>
                {
                    command.ExecuteNonQuery();
                }, true);
            }, ct);
        }

        /// <summary>
        /// Asynchronously checks if a materialized view exists in PostgreSQL using pg_matviews.
        /// </summary>
        /// <param name="viewName">The name of the materialized view to check.</param>
        /// <param name="ct">Cancellation token.</param>
        public Task<bool> MaterializedViewExistsAsync(string viewName, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(viewName))
                throw new System.ArgumentException("View name cannot be null or empty.", nameof(viewName));

            return Task.Run(() =>
            {
                bool exists = false;
                DoCommand((command) =>
                {
                    command.CommandText = "SELECT 1 FROM pg_matviews WHERE matviewname = @viewName AND schemaname = current_schema()";
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
            }, ct);
        }
    }
}
