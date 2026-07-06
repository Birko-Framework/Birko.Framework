namespace Birko.Data.SQL.PostgreSQL.Stores
{
    /// <summary>
    /// Configuration for <see cref="PostgreSQLStoreFactory"/> — the connection essentials plus
    /// PostgreSQL's binary-import toggle. Mirrors the SQLite factory-options pattern, minus the
    /// file-path resolution. The factory builds one shared <see cref="PostgreSqlSettings"/> from these.
    /// </summary>
    public class PostgreSQLStoreFactoryOptions
    {
        /// <summary>Server host.</summary>
        public string Location { get; set; } = string.Empty;

        /// <summary>Database name.</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>Login user id.</summary>
        public string? UserName { get; set; }

        /// <summary>Login password.</summary>
        public string? Password { get; set; }

        /// <summary>TCP port. Default is 5432.</summary>
        public int Port { get; set; } = 5432;

        /// <summary>Whether to require an encrypted connection. Default is false.</summary>
        public bool UseSecure { get; set; } = false;

        /// <summary>Command timeout in seconds. Default is 30.</summary>
        public int CommandTimeout { get; set; } = 30;

        /// <summary>Use the binary COPY import path for bulk insert. Default is true.</summary>
        public bool UseBinaryImport { get; set; } = true;
    }
}
