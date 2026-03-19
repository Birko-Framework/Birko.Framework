namespace Birko.Data.SQL.Connectors
{
    public partial class SqLiteConnector
    {
        /// <summary>
        /// Builds the CREATE VIEW SQL for SQLite.
        /// SQLite does not support CREATE OR REPLACE VIEW, uses IF NOT EXISTS instead.
        /// </summary>
        protected override string BuildCreateViewSql(string viewName, string selectSql)
        {
            return "CREATE VIEW IF NOT EXISTS " + QuoteIdentifier(viewName) + " AS " + selectSql;
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
    }
}
