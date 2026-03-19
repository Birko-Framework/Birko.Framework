namespace Birko.Data.SQL.Connectors
{
    public partial class MySQLConnector
    {
        // MySQL natively supports CREATE OR REPLACE VIEW (the default in AbstractConnectorBase).
        // No override needed for BuildCreateViewSql.

        /// <summary>
        /// Checks if a view exists in MySQL using information_schema.
        /// </summary>
        public override bool ViewExists(string viewName)
        {
            if (string.IsNullOrWhiteSpace(viewName))
                throw new System.ArgumentException("View name cannot be null or empty.", nameof(viewName));

            bool exists = false;
            DoCommand((command) =>
            {
                command.CommandText = "SELECT 1 FROM information_schema.VIEWS WHERE TABLE_NAME = @viewName AND TABLE_SCHEMA = DATABASE()";
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
