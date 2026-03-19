namespace Birko.Data.SQL.Connectors
{
    public partial class PostgreSQLConnector
    {
        // PostgreSQL natively supports CREATE OR REPLACE VIEW (the default in AbstractConnectorBase).
        // No override needed for BuildCreateViewSql.

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
                command.CommandText = "SELECT 1 FROM information_schema.views WHERE table_name = @viewName";
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
                command.CommandText = "CREATE MATERIALIZED VIEW IF NOT EXISTS " + QuoteIdentifier(name!) + " AS " + selectSql;
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
                command.CommandText = concurrently
                    ? "REFRESH MATERIALIZED VIEW CONCURRENTLY " + QuoteIdentifier(viewName)
                    : "REFRESH MATERIALIZED VIEW " + QuoteIdentifier(viewName);
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
                command.CommandText = "DROP MATERIALIZED VIEW IF EXISTS " + QuoteIdentifier(viewName);
            }, (command) =>
            {
                command.ExecuteNonQuery();
            }, true);
        }
    }
}
