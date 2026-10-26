namespace Birko.Xaml.Core.Diff;

/// <summary>
/// One step of a JSON path: an object key or an array index (web <c>JsonPathSegment = string | number</c>). A key
/// <c>"0"</c> and an index <c>0</c> are different segments, as on the web: <c>["0"]</c> is not <c>[0]</c>.
/// </summary>
public readonly struct JsonPathSegment : IEquatable<JsonPathSegment>
{
    private readonly string? _key;

    private JsonPathSegment(string? key, int index)
    {
        _key = key;
        Index = index;
    }

    /// <summary>The object key, or <c>null</c> for an index.</summary>
    public string? Key => _key;

    /// <summary>The array index; meaningful only when <see cref="IsIndex"/>.</summary>
    public int Index { get; }

    public bool IsIndex => _key is null;

    public static JsonPathSegment ForKey(string key) => new(key ?? throw new ArgumentNullException(nameof(key)), 0);

    public static JsonPathSegment ForIndex(int index) => new(null, index);

    public static implicit operator JsonPathSegment(string key) => ForKey(key);

    public static implicit operator JsonPathSegment(int index) => ForIndex(index);

    public bool Equals(JsonPathSegment other) => IsIndex ? other.IsIndex && Index == other.Index : _key == other._key;

    public override bool Equals(object? obj) => obj is JsonPathSegment s && Equals(s);

    public override int GetHashCode() => IsIndex ? Index.GetHashCode() : _key!.GetHashCode(StringComparison.Ordinal);

    public static bool operator ==(JsonPathSegment a, JsonPathSegment b) => a.Equals(b);

    public static bool operator !=(JsonPathSegment a, JsonPathSegment b) => !a.Equals(b);

    public override string ToString() => IsIndex ? $"[{Index}]" : _key!;
}
