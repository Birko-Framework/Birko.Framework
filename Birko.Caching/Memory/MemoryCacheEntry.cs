using System;

namespace Birko.Caching.Memory;

internal sealed class MemoryCacheEntry
{
    public object? Value { get; }
    /// <summary>The T the value was stored as; the only type information a stored null has (SH-M039).</summary>
    public Type StoredType { get; }
    public CacheEntryOptions Options { get; }
    public DateTime CreatedAt { get; }
    public DateTime LastAccessedAt { get; set; }

    public MemoryCacheEntry(object? value, Type storedType, CacheEntryOptions options)
    {
        Value = value;
        StoredType = storedType;
        Options = options;
        CreatedAt = DateTime.UtcNow;
        LastAccessedAt = DateTime.UtcNow;
    }

    public bool IsExpired()
    {
        var now = DateTime.UtcNow;

        if (Options.AbsoluteExpiration.HasValue && now - CreatedAt > Options.AbsoluteExpiration.Value)
            return true;

        if (Options.SlidingExpiration.HasValue && now - LastAccessedAt > Options.SlidingExpiration.Value)
            return true;

        return false;
    }
}
