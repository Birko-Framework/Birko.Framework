using System.Text.Json;
using System.Text.Json.Serialization;
using Birko.Serialization;
using Birko.Serialization.Json;

namespace Birko.Caching.Serialization;

/// <summary>
/// JSON serializer for cache values. Used by distributed cache backends (Redis, etc.)
/// to serialize/deserialize complex objects.
/// Delegates to <see cref="ISerializer"/> for pluggable serialization format support.
/// </summary>
public static class CacheSerializer
{
    private static readonly ISerializer DefaultSerializer = new SystemJsonSerializer(new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    });

    public static byte[] Serialize<T>(T value)
    {
        return DefaultSerializer.SerializeToBytes(value!);
    }

    public static T? Deserialize<T>(byte[] data)
    {
        return DefaultSerializer.DeserializeFromBytes<T>(data);
    }

    public static string SerializeToString<T>(T value)
    {
        return DefaultSerializer.Serialize(value!);
    }

    public static T? DeserializeFromString<T>(string data)
    {
        return DefaultSerializer.Deserialize<T>(data);
    }
}
