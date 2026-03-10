using System;

namespace Birko.Caching;

public enum CachePriority
{
    Low,
    Normal,
    High,
    NeverRemove
}

/// <summary>
/// Options controlling cache entry expiration and eviction behavior.
/// </summary>
public class CacheEntryOptions
{
    /// <summary>
    /// Entry expires after this duration from creation, regardless of access.
    /// </summary>
    public TimeSpan? AbsoluteExpiration { get; set; }

    /// <summary>
    /// Entry expires if not accessed within this duration. Resets on each access.
    /// </summary>
    public TimeSpan? SlidingExpiration { get; set; }

    /// <summary>
    /// Eviction priority when cache is under memory pressure.
    /// </summary>
    public CachePriority Priority { get; set; } = CachePriority.Normal;

    public static CacheEntryOptions Absolute(TimeSpan ttl) => new() { AbsoluteExpiration = ttl };
    public static CacheEntryOptions Sliding(TimeSpan window) => new() { SlidingExpiration = window };
    public static CacheEntryOptions AbsoluteAndSliding(TimeSpan ttl, TimeSpan sliding) => new() { AbsoluteExpiration = ttl, SlidingExpiration = sliding };

    /// <summary>
    /// Default options: 5 minute absolute expiration.
    /// </summary>
    public static CacheEntryOptions Default => Absolute(TimeSpan.FromMinutes(5));
}
