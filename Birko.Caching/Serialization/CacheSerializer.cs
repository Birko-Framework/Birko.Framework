using System.Text.Json;
using System.Text.Json.Serialization;

namespace Birko.Caching.Serialization;

/// <summary>
/// JSON serializer for cache values. Used by distributed cache backends (Redis, etc.)
/// to serialize/deserialize complex objects.
/// </summary>
public static class CacheSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    public static byte[] Serialize<T>(T value)
    {
        return JsonSerializer.SerializeToUtf8Bytes(value, Options);
    }

    public static T? Deserialize<T>(byte[] data)
    {
        return JsonSerializer.Deserialize<T>(data, Options);
    }

    public static string SerializeToString<T>(T value)
    {
        return JsonSerializer.Serialize(value, Options);
    }

    public static T? DeserializeFromString<T>(string data)
    {
        return JsonSerializer.Deserialize<T>(data, Options);
    }
}
