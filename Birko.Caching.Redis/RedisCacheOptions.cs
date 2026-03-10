using System;

namespace Birko.Caching.Redis;

/// <summary>
/// Configuration options for Redis cache backend.
/// </summary>
public class RedisCacheOptions
{
    /// <summary>
    /// Redis connection string (e.g., "localhost:6379", "redis:6379,password=secret").
    /// </summary>
    public string ConnectionString { get; set; } = "localhost:6379";

    /// <summary>
    /// Instance name prefix for all keys. Enables multiple apps to share one Redis instance.
    /// Keys are stored as "{InstanceName}:{key}".
    /// </summary>
    public string? InstanceName { get; set; }

    /// <summary>
    /// Default expiration for entries without explicit options.
    /// </summary>
    public TimeSpan DefaultExpiration { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Redis database index (0-15). Default: 0.
    /// </summary>
    public int Database { get; set; } = 0;
}
