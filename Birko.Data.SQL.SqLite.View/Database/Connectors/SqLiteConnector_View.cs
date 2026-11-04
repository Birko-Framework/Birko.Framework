namespace Birko.Data.SQL.Connectors
{
    public partial class SqLiteConnector
    {
        /// <summary>
        /// Builds the CREATE VIEW SQL for SQLite, which has no <c>CREATE OR REPLACE VIEW</c>: the old definition is
        /// dropped and the new one created in one command, which <c>CreateView</c> runs in its own transaction, so a
        /// refused definition leaves the old one in place.
        /// <para>
        /// TASK-717: this was <c>CREATE VIEW IF NOT EXISTS</c> (CR-L193 documented it), so a second definition under
        /// one name was kept as the first with no error (measured) while every other provider replaced it.
        /// </para>
        /// </summary>
        protected override string BuildCreateViewSql(string viewName, string selectSql)
        {
            var name = QuoteIdentifier(viewName);
            return "DROP VIEW IF EXISTS " + name + "; CREATE VIEW " + name + " AS " + selectSql;
        }

        /// <summary>
        /// Checks if a view exists in SQLite using sqlite_master.
        /// </summary>
        public override bool ViewExists(string viewName)
        {
            if (string.IsNullOrWhiteSpace(viewName))
                throw new System.ArgumentException("View name cannot be null or empty.", nameof(viewName));

            bool exists = false;
            DoCommand((command) =>
            {
                command.CommandText = "SELECT 1 FROM sqlite_master WHERE type = 'view' AND name = @viewName";
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
        /// Checks if a view exists in SQLite (async) using sqlite_master. CR-M146: the base
        /// <c>ViewExistsAsync</c> probes with <c>SELECT 1 FROM "name" WHERE 1=0</c> inside a catch-all,
        /// which returns true for a same-named TABLE and relies on exception control flow. This
        /// override mirrors the sync <see cref="ViewExists"/> — a parameterized <c>type='view'</c>
        /// lookup — and observes the cancellation token.
        /// </summary>
        public override async System.Threading.Tasks.Task<bool> ViewExistsAsync(string viewName, System.Threading.CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(viewName))
                throw new System.ArgumentException("View name cannot be null or empty.", nameof(viewName));

            bool exists = false;
            await DoCommandAsync(async (command) =>
            {
                command.CommandText = "SELECT 1 FROM sqlite_master WHERE type = 'view' AND name = @viewName";
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
