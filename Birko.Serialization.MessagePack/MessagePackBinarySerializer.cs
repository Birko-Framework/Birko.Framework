using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MessagePack;
using MessagePack.Resolvers;

namespace Birko.Serialization.MessagePack
{
    /// <summary>
    /// MessagePack binary serializer. Provides compact binary serialization for high-performance scenarios.
    /// </summary>
    /// <remarks>
    /// Requires NuGet package: MessagePack.
    /// Default: ContractlessStandardResolver (no [MessagePackObject] attributes needed).
    /// </remarks>
    public class MessagePackBinarySerializer : ISerializer
    {
        private readonly MessagePackSerializerOptions _options;

        public MessagePackBinarySerializer(MessagePackSerializerOptions? options = null)
        {
            _options = options ?? MessagePackSerializerOptions.Standard
                .WithResolver(ContractlessStandardResolver.Instance);
        }

        public string ContentType => "application/x-msgpack";

        public SerializationFormat Format => SerializationFormat.MessagePack;

        public string Serialize(object value)
        {
            ArgumentNullException.ThrowIfNull(value);
            var bytes = MessagePackSerializer.Serialize(value.GetType(), value, _options);
            return Convert.ToBase64String(bytes);
        }

        public string Serialize<T>(T value)
        {
            ArgumentNullException.ThrowIfNull(value);
            var bytes = MessagePackSerializer.Serialize(value, _options);
            return Convert.ToBase64String(bytes);
        }

        public object? Deserialize(string data, Type type)
        {
            ArgumentNullException.ThrowIfNull(data);
            ArgumentNullException.ThrowIfNull(type);
            var bytes = Convert.FromBase64String(data);
            return MessagePackSerializer.Deserialize(type, bytes, _options);
        }

        public T? Deserialize<T>(string data)
        {
            ArgumentNullException.ThrowIfNull(data);
            var bytes = Convert.FromBase64String(data);
            return MessagePackSerializer.Deserialize<T>(bytes, _options);
        }

        public byte[] SerializeToBytes(object value)
        {
            ArgumentNullException.ThrowIfNull(value);
            return MessagePackSerializer.Serialize(value.GetType(), value, _options);
        }

        public byte[] SerializeToBytes<T>(T value)
        {
            ArgumentNullException.ThrowIfNull(value);
            return MessagePackSerializer.Serialize(value, _options);
        }

        public object? DeserializeFromBytes(byte[] data, Type type)
        {
            ArgumentNullException.ThrowIfNull(data);
            ArgumentNullException.ThrowIfNull(type);
            return MessagePackSerializer.Deserialize(type, data, _options);
        }

        public T? DeserializeFromBytes<T>(byte[] data)
        {
            ArgumentNullException.ThrowIfNull(data);
            return MessagePackSerializer.Deserialize<T>(data, _options);
        }

        public void Serialize(Stream stream, object value)
        {
            ArgumentNullException.ThrowIfNull(stream);
            ArgumentNullException.ThrowIfNull(value);
            var bytes = MessagePackSerializer.Serialize(value.GetType(), value, _options);
            stream.Write(bytes, 0, bytes.Length);
        }

        public void Serialize<T>(Stream stream, T value)
        {
            ArgumentNullException.ThrowIfNull(stream);
            ArgumentNullException.ThrowIfNull(value);
            var bytes = MessagePackSerializer.Serialize(value, _options);
            stream.Write(bytes, 0, bytes.Length);
        }

        public object? Deserialize(Stream stream, Type type)
        {
            ArgumentNullException.ThrowIfNull(stream);
            ArgumentNullException.ThrowIfNull(type);
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            return MessagePackSerializer.Deserialize(type, ms.ToArray(), _options);
        }

        public T? Deserialize<T>(Stream stream)
        {
            ArgumentNullException.ThrowIfNull(stream);
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            return MessagePackSerializer.Deserialize<T>(ms.ToArray(), _options);
        }

        public async Task SerializeAsync(Stream stream, object value, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(stream);
            ArgumentNullException.ThrowIfNull(value);
            var bytes = MessagePackSerializer.Serialize(value.GetType(), value, _options);
            await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
        }

        public async Task SerializeAsync<T>(Stream stream, T value, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(stream);
            ArgumentNullException.ThrowIfNull(value);
            var bytes = MessagePackSerializer.Serialize(value, _options);
            await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
        }

        public async Task<object?> DeserializeAsync(Stream stream, Type type, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(stream);
            ArgumentNullException.ThrowIfNull(type);
            using var ms = new MemoryStream();
            await stream.CopyToAsync(ms, cancellationToken).ConfigureAwait(false);
            return MessagePackSerializer.Deserialize(type, ms.ToArray(), _options);
        }

        public async Task<T?> DeserializeAsync<T>(Stream stream, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(stream);
            using var ms = new MemoryStream();
            await stream.CopyToAsync(ms, cancellationToken).ConfigureAwait(false);
            return MessagePackSerializer.Deserialize<T>(ms.ToArray(), _options);
        }
    }
}
