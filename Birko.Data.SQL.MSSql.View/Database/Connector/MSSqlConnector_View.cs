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
    }
}
