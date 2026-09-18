using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Birko.Serialization.Yaml
{
    /// <summary>
    /// YAML serializer using YamlDotNet. Produces human-readable text suitable for
    /// configuration files, CI manifests, and document interchange.
    /// </summary>
    /// <remarks>
    /// Requires NuGet package: YamlDotNet.
    /// Default: camelCase naming, default scalar style; pass custom <see cref="ISerializer"/> /
    /// <see cref="IDeserializer"/> via the constructor to override.
    /// </remarks>
    public class YamlDotNetSerializer : Birko.Serialization.ISerializer
    {
        private readonly YamlDotNet.Serialization.ISerializer _yamlSerializer;
        private readonly YamlDotNet.Serialization.IDeserializer _yamlDeserializer;

        public YamlDotNetSerializer(
            YamlDotNet.Serialization.ISerializer? serializer = null,
            YamlDotNet.Serialization.IDeserializer? deserializer = null)
        {
            _yamlSerializer = serializer ?? new SerializerBuilder()
                .WithNamingConvention(CamelCaseNamingConvention.Instance)
                .Build();
            _yamlDeserializer = deserializer ?? new DeserializerBuilder()
                .WithNamingConvention(CamelCaseNamingConvention.Instance)
                .IgnoreUnmatchedProperties()
                .Build();
        }

        public string ContentType => "application/yaml";

        public SerializationFormat Format => SerializationFormat.Yaml;

        public string Serialize(object value)
        {
            ArgumentNullException.ThrowIfNull(value);
            return _yamlSerializer.Serialize(value, value.GetType());
        }

        public string Serialize<T>(T value)
        {
            ArgumentNullException.ThrowIfNull(value);
            return _yamlSerializer.Serialize(value!);
        }

        public object? Deserialize(string data, Type type)
        {
            ArgumentNullException.ThrowIfNull(data);
            ArgumentNullException.ThrowIfNull(type);
            return _yamlDeserializer.Deserialize(data, type);
        }

        public T? Deserialize<T>(string data)
        {
            ArgumentNullException.ThrowIfNull(data);
            return _yamlDeserializer.Deserialize<T>(data);
        }

        public byte[] SerializeToBytes(object value)
        {
            var yaml = Serialize(value);
            return Encoding.UTF8.GetBytes(yaml);
        }

        public byte[] SerializeToBytes<T>(T value)
        {
            var yaml = Serialize<T>(value);
            return Encoding.UTF8.GetBytes(yaml);
        }

        public object? DeserializeFromBytes(byte[] data, Type type)
        {
            ArgumentNullException.ThrowIfNull(data);
            var yaml = Encoding.UTF8.GetString(data);
            return Deserialize(yaml, type);
        }

        public T? DeserializeFromBytes<T>(byte[] data)
        {
            ArgumentNullException.ThrowIfNull(data);
            var yaml = Encoding.UTF8.GetString(data);
            return Deserialize<T>(yaml);
        }

        public void Serialize(Stream stream, object value)
        {
            ArgumentNullException.ThrowIfNull(stream);
            ArgumentNullException.ThrowIfNull(value);
            using var writer = new StreamWriter(stream, Encoding.UTF8, bufferSize: 1024, leaveOpen: true);
            _yamlSerializer.Serialize(writer, value, value.GetType());
            writer.Flush();
        }

        public void Serialize<T>(Stream stream, T value)
        {
            ArgumentNullException.ThrowIfNull(stream);
            ArgumentNullException.ThrowIfNull(value);
            using var writer = new StreamWriter(stream, Encoding.UTF8, bufferSize: 1024, leaveOpen: true);
            _yamlSerializer.Serialize(writer, value!);
            writer.Flush();
        }

        public object? Deserialize(Stream stream, Type type)
        {
            ArgumentNullException.ThrowIfNull(stream);
            ArgumentNullException.ThrowIfNull(type);
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 1024, leaveOpen: true);
            return _yamlDeserializer.Deserialize(reader, type);
        }

        public T? Deserialize<T>(Stream stream)
        {
            ArgumentNullException.ThrowIfNull(stream);
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 1024, leaveOpen: true);
            return _yamlDeserializer.Deserialize<T>(reader);
        }

        // CR-L365: YamlDotNet has no async API, so these are sync-wrapped. Observe the token up front so a
        // pre-cancelled token surfaces OperationCanceledException before doing the (synchronous) work.
        public Task SerializeAsync(Stream stream, object value, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(stream);
            ArgumentNullException.ThrowIfNull(value);
            cancellationToken.ThrowIfCancellationRequested();
            Serialize(stream, value);
            return Task.CompletedTask;
        }

        public Task SerializeAsync<T>(Stream stream, T value, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(stream);
            ArgumentNullException.ThrowIfNull(value);
            cancellationToken.ThrowIfCancellationRequested();
            Serialize<T>(stream, value);
            return Task.CompletedTask;
        }

        public Task<object?> DeserializeAsync(Stream stream, Type type, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(stream);
            ArgumentNullException.ThrowIfNull(type);
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Deserialize(stream, type));
        }

        public Task<T?> DeserializeAsync<T>(Stream stream, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(stream);
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Deserialize<T>(stream));
        }
    }
}
