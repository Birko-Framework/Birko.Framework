namespace Birko.Data.SQL.Connectors
{
    public partial class MySQLConnector
    {
        // MySQL natively supports CREATE OR REPLACE VIEW (the default in AbstractConnectorBase).
        // No override needed for BuildCreateViewSql.

        private const string ViewExistsSql =
            "SELECT 1 FROM information_schema.VIEWS WHERE TABLE_NAME = @viewName AND TABLE_SCHEMA = DATABASE()";

        /// <summary>
        /// Checks if a view exists in MySQL using information_schema (scoped to the current DATABASE()).
        /// </summary>
        public override bool ViewExists(string viewName)
        {
            if (string.IsNullOrWhiteSpace(viewName))
                throw new System.ArgumentException("View name cannot be null or empty.", nameof(viewName));

            bool exists = false;
            DoCommand((command) =>
            {
                command.CommandText = ViewExistsSql;
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
        /// Async parity for <see cref="ViewExists"/> (CR-L186): runs the same parameterized
        /// information_schema query scoped to DATABASE() via DoCommandAsync (observing the token),
        /// instead of the base fallback's `SELECT 1 FROM &lt;view&gt; WHERE 1=0` in a try/catch, which
        /// swallows connection/permission errors as "view does not exist" and is not database-scoped.
        /// </summary>
        public override async System.Threading.Tasks.Task<bool> ViewExistsAsync(
            string viewName, System.Threading.CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(viewName))
                throw new System.ArgumentException("View name cannot be null or empty.", nameof(viewName));

            bool exists = false;
            await DoCommandAsync(async (command) =>
            {
                command.CommandText = ViewExistsSql;
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
