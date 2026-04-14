using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Birko.Serialization.Json
{
    /// <summary>
    /// JSON serializer using System.Text.Json (built-in, no external dependencies).
    /// </summary>
    public class SystemJsonSerializer : ISerializer
    {
        private readonly JsonSerializerOptions _options;
        private readonly JsonWriterOptions _writerOptions;

        public SystemJsonSerializer(JsonSerializerOptions? options = null, JsonWriterOptions? writerOptions = null)
        {
            _options = options ?? new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = false
            };
            _writerOptions = writerOptions ?? new JsonWriterOptions
            {
                Indented = false
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

        public void Serialize(Stream stream, object value)
        {
            ArgumentNullException.ThrowIfNull(stream);
            ArgumentNullException.ThrowIfNull(value);
            JsonSerializer.Serialize(stream, value, value.GetType(), _options);
        }

        public void Serialize<T>(Stream stream, T value)
        {
            ArgumentNullException.ThrowIfNull(stream);
            ArgumentNullException.ThrowIfNull(value);
            using var jsonWriter = new Utf8JsonWriter(stream, _writerOptions);
            JsonSerializer.Serialize(jsonWriter, value, _options);
        }

        public object? Deserialize(Stream stream, Type type)
        {
            ArgumentNullException.ThrowIfNull(stream);
            ArgumentNullException.ThrowIfNull(type);
            return JsonSerializer.Deserialize(stream, type, _options);
        }

        public T? Deserialize<T>(Stream stream)
        {
            ArgumentNullException.ThrowIfNull(stream);
            return JsonSerializer.Deserialize<T>(stream, _options);
        }

        public async Task SerializeAsync(Stream stream, object value, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(stream);
            ArgumentNullException.ThrowIfNull(value);
            await JsonSerializer.SerializeAsync(stream, value, value.GetType(), _options, cancellationToken).ConfigureAwait(false);
        }

        public async Task SerializeAsync<T>(Stream stream, T value, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(stream);
            ArgumentNullException.ThrowIfNull(value);
            await JsonSerializer.SerializeAsync(stream, value, _options, cancellationToken).ConfigureAwait(false);
        }

        public async Task<object?> DeserializeAsync(Stream stream, Type type, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(stream);
            ArgumentNullException.ThrowIfNull(type);
            return await JsonSerializer.DeserializeAsync(stream, type, _options, cancellationToken).ConfigureAwait(false);
        }

        public async Task<T?> DeserializeAsync<T>(Stream stream, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(stream);
            return await JsonSerializer.DeserializeAsync<T>(stream, _options, cancellationToken).ConfigureAwait(false);
        }
    }
}
