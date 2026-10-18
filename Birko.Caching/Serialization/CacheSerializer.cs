using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using Birko.Serialization;
using Birko.Serialization.Json;

namespace Birko.Caching.Serialization;

/// <summary>
/// JSON serializer for cache values. Used by distributed cache backends (Redis, etc.)
/// to serialize/deserialize complex objects.
/// </summary>
/// <remarks>
/// The format is fixed: System.Text.Json, camelCase, nulls omitted. SH-L046: this used to say it delegated to
/// <see cref="ISerializer"/> "for pluggable format support", but nothing can plug one in (the serializer is a private
/// static); the <see cref="ISerializer"/> underneath is an implementation detail.
/// <para>
/// A <c>null</c> value is a value (SH-L045, decided by the user 2026-10-18: a stored null is a hit on every backend, as
/// MemoryCache's CR-L036). It serializes to the JSON <c>null</c> and deserializes to <c>default</c>; the serializer
/// underneath refuses null, so it never reaches it.
/// </para>
/// </remarks>
public static class CacheSerializer
{
    private static readonly ISerializer DefaultSerializer = new SystemJsonSerializer(new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    });

    private const string JsonNull = "null";
    private static readonly byte[] JsonNullBytes = "null"u8.ToArray();

    public static byte[] Serialize<T>(T value)
    {
        if (value is null) return (byte[])JsonNullBytes.Clone();
        return DefaultSerializer.SerializeToBytes(value);
    }

    public static T? Deserialize<T>(byte[] data)
    {
        if (data.AsSpan().SequenceEqual(JsonNullBytes)) return default;
        return DefaultSerializer.DeserializeFromBytes<T>(data);
    }

    public static string SerializeToString<T>(T value)
    {
        if (value is null) return JsonNull;
        return DefaultSerializer.Serialize(value);
    }

    public static T? DeserializeFromString<T>(string data)
    {
        if (data == JsonNull) return default;
        return DefaultSerializer.Deserialize<T>(data);
    }
}
