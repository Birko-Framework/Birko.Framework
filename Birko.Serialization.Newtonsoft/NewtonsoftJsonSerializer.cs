using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace Birko.Serialization.Newtonsoft
{
    /// <summary>
    /// JSON serializer using Newtonsoft.Json. Useful when interoperating with APIs or libraries
    /// that require Newtonsoft-specific features (e.g., JsonProperty attributes, custom converters).
    /// </summary>
    public class NewtonsoftJsonSerializer : ISerializer
    {
        private readonly JsonSerializerSettings _settings;

        public NewtonsoftJsonSerializer(JsonSerializerSettings? settings = null)
        {
            _settings = settings ?? new JsonSerializerSettings
            {
                ContractResolver = new global::Newtonsoft.Json.Serialization.CamelCasePropertyNamesContractResolver(),
                NullValueHandling = NullValueHandling.Ignore,
                Formatting = Formatting.None
            };
        }

        public string ContentType => "application/json";

        public SerializationFormat Format => SerializationFormat.Json;

        public string Serialize(object value)
        {
            ArgumentNullException.ThrowIfNull(value);
            return JsonConvert.SerializeObject(value, value.GetType(), _settings);
        }

        public string Serialize<T>(T value)
        {
            ArgumentNullException.ThrowIfNull(value);
            return JsonConvert.SerializeObject(value, _settings);
        }

        public object? Deserialize(string data, Type type)
        {
            ArgumentNullException.ThrowIfNull(data);
            ArgumentNullException.ThrowIfNull(type);
            return JsonConvert.DeserializeObject(data, type, _settings);
        }

        public T? Deserialize<T>(string data)
        {
            ArgumentNullException.ThrowIfNull(data);
            return JsonConvert.DeserializeObject<T>(data, _settings);
        }

        public byte[] SerializeToBytes(object value)
        {
            var json = Serialize(value);
            return Encoding.UTF8.GetBytes(json);
        }

        public byte[] SerializeToBytes<T>(T value)
        {
            var json = Serialize<T>(value);
            return Encoding.UTF8.GetBytes(json);
        }

        public object? DeserializeFromBytes(byte[] data, Type type)
        {
            ArgumentNullException.ThrowIfNull(data);
            var json = Encoding.UTF8.GetString(data);
            return Deserialize(json, type);
        }

        public T? DeserializeFromBytes<T>(byte[] data)
        {
            ArgumentNullException.ThrowIfNull(data);
            var json = Encoding.UTF8.GetString(data);
            return Deserialize<T>(json);
        }

        public void Serialize(Stream stream, object value)
        {
            ArgumentNullException.ThrowIfNull(stream);
            ArgumentNullException.ThrowIfNull(value);
            using var streamWriter = new StreamWriter(stream, Encoding.UTF8, 1024, leaveOpen: true);
            using var jsonWriter = new JsonTextWriter(streamWriter);
            var serializer = JsonSerializer.Create(_settings);
            serializer.Serialize(jsonWriter, value);
            jsonWriter.Flush();
        }

        public void Serialize<T>(Stream stream, T value)
        {
            ArgumentNullException.ThrowIfNull(stream);
            ArgumentNullException.ThrowIfNull(value);
            using var streamWriter = new StreamWriter(stream, Encoding.UTF8, 1024, leaveOpen: true);
            using var jsonWriter = new JsonTextWriter(streamWriter);
            var serializer = JsonSerializer.Create(_settings);
            serializer.Serialize(jsonWriter, value);
            jsonWriter.Flush();
        }

        public object? Deserialize(Stream stream, Type type)
        {
            ArgumentNullException.ThrowIfNull(stream);
            ArgumentNullException.ThrowIfNull(type);
            using var streamReader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 1024, leaveOpen: true);
            using var jsonReader = new JsonTextReader(streamReader);
            var serializer = JsonSerializer.Create(_settings);
            return serializer.Deserialize(jsonReader, type);
        }

        public T? Deserialize<T>(Stream stream)
        {
            ArgumentNullException.ThrowIfNull(stream);
            using var streamReader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 1024, leaveOpen: true);
            using var jsonReader = new JsonTextReader(streamReader);
            var serializer = JsonSerializer.Create(_settings);
            return serializer.Deserialize<T>(jsonReader);
        }

        public async Task SerializeAsync(Stream stream, object value, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(stream);
            ArgumentNullException.ThrowIfNull(value);
            using var streamWriter = new StreamWriter(stream, Encoding.UTF8, 1024, leaveOpen: true);
            using var jsonWriter = new JsonTextWriter(streamWriter);
            var serializer = JsonSerializer.Create(_settings);
            serializer.Serialize(jsonWriter, value);
            await jsonWriter.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        public async Task SerializeAsync<T>(Stream stream, T value, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(stream);
            ArgumentNullException.ThrowIfNull(value);
            using var streamWriter = new StreamWriter(stream, Encoding.UTF8, 1024, leaveOpen: true);
            using var jsonWriter = new JsonTextWriter(streamWriter);
            var serializer = JsonSerializer.Create(_settings);
            serializer.Serialize(jsonWriter, value);
            await jsonWriter.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        public async Task<object?> DeserializeAsync(Stream stream, Type type, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(stream);
            ArgumentNullException.ThrowIfNull(type);
            using var streamReader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 1024, leaveOpen: true);
            using var jsonReader = new JsonTextReader(streamReader);
            var serializer = JsonSerializer.Create(_settings);
            return await Task.Run(() => serializer.Deserialize(jsonReader, type), cancellationToken).ConfigureAwait(false);
        }

        public async Task<T?> DeserializeAsync<T>(Stream stream, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(stream);
            using var streamReader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 1024, leaveOpen: true);
            using var jsonReader = new JsonTextReader(streamReader);
            var serializer = JsonSerializer.Create(_settings);
            return await Task.Run(() => serializer.Deserialize<T>(jsonReader), cancellationToken).ConfigureAwait(false);
        }
    }
}
