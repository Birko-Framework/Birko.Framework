using Birko.Data.SQL.Stores;

namespace Birko.Data.Migrations.SQL.Settings
{
    /// <summary>
    /// Settings for SQL migration runners.
    /// Extends SqlSettings to inherit connection timeout and command timeout configuration.
    /// </summary>
    public class SqlMigrationSettings : SqlSettings
    {
        /// <summary>
        /// Gets or sets the name of the migrations table.
        /// Default is "__Migrations".
        /// </summary>
        public string MigrationsTable { get; set; } = "__Migrations";

        /// <summary>
        /// Gets or sets the schema for the migrations table.
        /// Default is null (uses default schema).
        /// </summary>
        public string? Schema { get; set; }

        /// <summary>
        /// Gets or sets whether to use transactions during migrations.
        /// Default is true.
        /// </summary>
        public bool UseTransaction { get; set; } = true;

        /// <summary>
        /// Gets or sets the transaction timeout in seconds.
        /// Default is 30 seconds.
        /// </summary>
        public int TransactionTimeout { get; set; } = 30;

        /// <summary>
        /// Gets the full qualified name of the migrations table including schema.
        /// </summary>
        public string FullTableName
        {
            get
            {
                return string.IsNullOrEmpty(Schema)
                    ? QuoteIdentifier(MigrationsTable)
                    : $"{QuoteIdentifier(Schema)}.{QuoteIdentifier(MigrationsTable)}";
            }
        }

        /// <summary>
        /// Quotes an identifier for SQL safety.
        /// Override in provider-specific settings if needed.
        /// </summary>
        protected virtual string QuoteIdentifier(string identifier)
        {
            return "\"" + identifier.Replace("\"", "\"\"") + "\"";
        }
    }
}
