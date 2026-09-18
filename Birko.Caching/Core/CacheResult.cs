namespace Birko.Caching;

/// <summary>
/// Result of a cache lookup. Distinguishes between "not found" and "found but null value".
/// </summary>
public readonly struct CacheResult<T>
{
    public bool HasValue { get; }
    public T? Value { get; }

    private CacheResult(bool hasValue, T? value)
    {
        HasValue = hasValue;
        Value = value;
    }

    public static CacheResult<T> Hit(T value) => new(true, value);
    public static CacheResult<T> Miss() => new(false, default);
}
