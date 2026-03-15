namespace Birko.Storage;

/// <summary>
/// Result of a storage lookup. Distinguishes "not found" from a successful result.
/// </summary>
public readonly struct StorageResult<T>
{
    public bool Found { get; }
    public T? Value { get; }

    private StorageResult(bool found, T? value)
    {
        Found = found;
        Value = value;
    }

    public static StorageResult<T> Success(T value) => new(true, value);
    public static StorageResult<T> NotFound() => new(false, default);
}
