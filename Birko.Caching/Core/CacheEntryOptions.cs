using System;

namespace Birko.Caching;

/// <summary>
/// An entry's priority, carried with its options.
/// </summary>
/// <remarks>
/// SH-L040: no backend evicts under memory pressure or has a size limit, so no value changes when an entry is removed:
/// every backend removes an expired entry whatever its priority (MemoryCache's sweep included, since SH-L039), and
/// RedisCache does not store it. Hybrid copies it to its L1 options. It is kept for source compatibility and for a backend
/// that may honour it.
/// </remarks>
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
    /// The entry's priority. Carried only: no backend evicts under memory pressure (see <see cref="CachePriority"/>).
    /// </summary>
    public CachePriority Priority { get; set; } = CachePriority.Normal;

    /// <exception cref="ArgumentOutOfRangeException"><paramref name="ttl"/> is zero or negative (SH-L041).</exception>
    public static CacheEntryOptions Absolute(TimeSpan ttl)
        => Validate(new CacheEntryOptions { AbsoluteExpiration = ttl }, nameof(ttl))!;

    /// <exception cref="ArgumentOutOfRangeException"><paramref name="window"/> is zero or negative (SH-L041).</exception>
    public static CacheEntryOptions Sliding(TimeSpan window)
        => Validate(new CacheEntryOptions { SlidingExpiration = window }, nameof(window))!;

    /// <exception cref="ArgumentOutOfRangeException">Either duration is zero or negative (SH-L041).</exception>
    public static CacheEntryOptions AbsoluteAndSliding(TimeSpan ttl, TimeSpan sliding)
    {
        Validate(new CacheEntryOptions { SlidingExpiration = sliding }, nameof(sliding));
        return Validate(new CacheEntryOptions { AbsoluteExpiration = ttl, SlidingExpiration = sliding }, nameof(ttl))!;
    }

    /// <summary>
    /// Refuses options whose absolute or sliding expiration is zero or negative, and returns them otherwise. Every
    /// backend calls it on the options a write receives, because the properties are settable after construction.
    /// </summary>
    /// <remarks>
    /// SH-L041, decided by the user 2026-10-18: refuse up front. A non-positive window used to be a silent no-op on
    /// MemoryCache (an entry already expired) and a server error on Redis (a non-positive PX), so the same call
    /// succeeded on one backend and threw on another. A null <paramref name="options"/> passes (it means the default).
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">An expiration is zero or negative.</exception>
    public static CacheEntryOptions? Validate(CacheEntryOptions? options, string paramName = "options")
    {
        if (options == null) return null;
        if (options.AbsoluteExpiration is { } ttl && ttl <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(paramName, ttl, "An absolute expiration must be positive.");
        if (options.SlidingExpiration is { } window && window <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(paramName, window, "A sliding expiration must be positive.");
        return options;
    }

    /// <summary>
    /// Default options: 5 minute absolute expiration.
    /// </summary>
    public static CacheEntryOptions Default => Absolute(TimeSpan.FromMinutes(5));
}
