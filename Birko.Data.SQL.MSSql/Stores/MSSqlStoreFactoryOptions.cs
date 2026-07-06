namespace Birko.Data.SQL.MSSql.Stores
{
    /// <summary>
    /// Configuration for <see cref="MSSqlStoreFactory"/> — the connection essentials plus the
    /// SQL Server-specific toggles. Mirrors the SQLite factory-options pattern, minus the file-path
    /// resolution (a server database has no local path). The factory builds one shared
    /// <see cref="MSSqlSettings"/> from these.
    /// </summary>
    public class MSSqlStoreFactoryOptions
    {
        /// <summary>Server host (maps to <c>Server=tcp:{Location},{Port}</c>).</summary>
        public string Location { get; set; } = string.Empty;

        /// <summary>Database / initial catalog name.</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>SQL login user id.</summary>
        public string? UserName { get; set; }

        /// <summary>SQL login password.</summary>
        public string? Password { get; set; }

        /// <summary>TCP port. Default is 1433.</summary>
        public int Port { get; set; } = 1433;

        /// <summary>Whether to require an encrypted connection (<c>Encrypt=True</c>). Default is true.</summary>
        public bool UseSecure { get; set; } = true;

        /// <summary>Command timeout in seconds. Default is 30.</summary>
        public int CommandTimeout { get; set; } = 30;

        /// <summary>Enable Multiple Active Result Sets. Default is false.</summary>
        public bool MultipleActiveResultSets { get; set; } = false;

        /// <summary>Trust the server certificate (commonly needed for Azure SQL). Default is false.</summary>
        public bool TrustServerCertificate { get; set; } = false;
    }
}
