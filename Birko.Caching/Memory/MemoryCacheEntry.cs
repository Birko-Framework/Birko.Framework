using System;
using System.Threading;

namespace Birko.Caching.Memory;

internal sealed class MemoryCacheEntry
{
    private readonly TimeProvider _clock;
    private readonly long _createdTimestamp;
    // SH-L052: expiry is measured on the clock's MONOTONIC timestamp (TimeProvider.GetTimestamp), so a wall-clock step
    // (an NTP correction) neither extends nor cuts an entry's lifetime, and a test drives it through a fake TimeProvider.
    // The last access is a long read and written with Volatile, so the sweep never sees a torn value written by a
    // concurrent reader (a DateTime write is not atomic on a 32-bit runtime).
    private long _lastAccessedTimestamp;

    public object? Value { get; }
    /// <summary>The T the value was stored as; the only type information a stored null has (SH-M039).</summary>
    public Type StoredType { get; }
    public CacheEntryOptions Options { get; }

    public MemoryCacheEntry(object? value, Type storedType, CacheEntryOptions options, TimeProvider clock)
    {
        _clock = clock;
        Value = value;
        StoredType = storedType;
        Options = options;
        _createdTimestamp = clock.GetTimestamp();
        _lastAccessedTimestamp = _createdTimestamp;
    }

    /// <summary>Records an access now, by the cache's clock.</summary>
    public void Touch() => Volatile.Write(ref _lastAccessedTimestamp, _clock.GetTimestamp());

    public bool IsExpired()
    {
        if (Options.AbsoluteExpiration.HasValue && _clock.GetElapsedTime(_createdTimestamp) > Options.AbsoluteExpiration.Value)
            return true;

        if (Options.SlidingExpiration.HasValue
            && _clock.GetElapsedTime(Volatile.Read(ref _lastAccessedTimestamp)) > Options.SlidingExpiration.Value)
            return true;

        return false;
    }
}
