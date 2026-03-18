using System;
using System.Text.Json;

namespace Birko.Serialization.Json
{
    /// <summary>
    /// JSON serializer using System.Text.Json (built-in, no external dependencies).
    /// </summary>
    public class SystemJsonSerializer : ISerializer
    {
        private readonly JsonSerializerOptions _options;

        public SystemJsonSerializer(JsonSerializerOptions? options = null)
        {
            _options = options ?? new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = false
            };
        }

        public string ContentType => "application/json";

        public SerializationFormat Format => SerializationFormat.Json;

        public string Serialize(object value)
        {
            ArgumentNullException.ThrowIfNull(value);
            return JsonSerializer.Serialize(value, value.GetType(), _options);
        }

        public string Serialize<T>(T value)
        {
            ArgumentNullException.ThrowIfNull(value);
            return JsonSerializer.Serialize(value, _options);
        }

        public object? Deserialize(string data, Type type)
        {
            ArgumentNullException.ThrowIfNull(data);
            ArgumentNullException.ThrowIfNull(type);
            return JsonSerializer.Deserialize(data, type, _options);
        }

        public T? Deserialize<T>(string data)
        {
            ArgumentNullException.ThrowIfNull(data);
            return JsonSerializer.Deserialize<T>(data, _options);
        }

        public byte[] SerializeToBytes(object value)
        {
            ArgumentNullException.ThrowIfNull(value);
            return JsonSerializer.SerializeToUtf8Bytes(value, value.GetType(), _options);
        }

        public byte[] SerializeToBytes<T>(T value)
        {
            ArgumentNullException.ThrowIfNull(value);
            return JsonSerializer.SerializeToUtf8Bytes(value, _options);
        }

        public object? DeserializeFromBytes(byte[] data, Type type)
        {
            ArgumentNullException.ThrowIfNull(data);
            ArgumentNullException.ThrowIfNull(type);
            return JsonSerializer.Deserialize(data, type, _options);
        }

        public T? DeserializeFromBytes<T>(byte[] data)
        {
            ArgumentNullException.ThrowIfNull(data);
            return JsonSerializer.Deserialize<T>(data, _options);
        }
    }
}
