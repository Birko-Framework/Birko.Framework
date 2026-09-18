namespace Birko.Data.SQL.MySQL.Stores
{
    /// <summary>
    /// Configuration for <see cref="MySQLStoreFactory"/> — the connection essentials plus MySQL's
    /// bulk-insert tuning. Mirrors the SQLite factory-options pattern, minus the file-path resolution.
    /// The factory builds one shared <see cref="MySqlSettings"/> from these.
    /// </summary>
    public class MySQLStoreFactoryOptions
    {
        /// <summary>Server host.</summary>
        public string Location { get; set; } = string.Empty;

        /// <summary>Database name.</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>Login user id.</summary>
        public string? UserName { get; set; }

        /// <summary>Login password.</summary>
        public string? Password { get; set; }

        /// <summary>TCP port. Default is 3306.</summary>
        public int Port { get; set; } = 3306;

        /// <summary>Whether to require an encrypted connection. Default is false.</summary>
        public bool UseSecure { get; set; } = false;

        /// <summary>Command timeout in seconds. Default is 30.</summary>
        public int CommandTimeout { get; set; } = 30;

        /// <summary>Batch size used by native bulk insert. Default is 1000.</summary>
        public int BulkInsertBatchSize { get; set; } = 1000;
    }
}
