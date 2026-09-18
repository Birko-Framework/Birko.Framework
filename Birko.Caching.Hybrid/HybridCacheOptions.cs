using System;

namespace Birko.Caching.Hybrid;

/// <summary>
/// Configuration options for the hybrid L1/L2 cache.
/// </summary>
public class HybridCacheOptions
{
    /// <summary>
    /// L1 (local memory) cache entry TTL. Entries in L1 expire faster than L2 to limit staleness.
    /// Default: 30 seconds.
    /// </summary>
    public TimeSpan L1DefaultExpiration { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Maximum absolute TTL for L1 entries, regardless of the requested cache options.
    /// Prevents local entries from living too long when L2 may have been invalidated by another node.
    /// Default: 5 minutes. Set to null to use the original entry options for L1.
    /// </summary>
    public TimeSpan? L1MaxExpiration { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// When true, SetAsync writes to both L1 and L2 simultaneously (write-through).
    /// When false, SetAsync writes to L2 first, then L1 (write-behind to L2 first for durability).
    /// Default: true.
    /// </summary>
    public bool WriteThrough { get; set; } = true;

    /// <summary>
    /// When true, a failed L2 operation falls back to L1 silently.
    /// When false, L2 failures propagate as exceptions.
    /// Default: true.
    /// </summary>
    public bool FallbackToL1OnL2Failure { get; set; } = true;
}
