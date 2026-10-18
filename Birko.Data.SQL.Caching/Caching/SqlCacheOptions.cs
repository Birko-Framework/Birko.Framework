using System;

namespace Birko.Data.SQL.Caching
{
    /// <summary>
    /// Configuration options for SQL query caching.
    /// </summary>
    public class SqlCacheOptions
    {
        /// <summary>
        /// Gets or sets the default cache expiration duration.
        /// Defaults to 5 minutes.
        /// </summary>
        public TimeSpan DefaultExpiration { get; set; } = TimeSpan.FromMinutes(5);

        /// <summary>
        /// Gets or sets whether caching is enabled.
        /// When disabled, reads go straight to the database and nothing is cached, but writes still invalidate the
        /// table in the cache, so another store sharing the cache and table is not left stale (SH-L054).
        /// Defaults to true.
        /// </summary>
        public bool Enabled { get; set; } = true;
    }
}
