using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ProtoBuf;

namespace Birko.Serialization.Protobuf
{
    /// <summary>
    /// Protocol Buffers binary serializer using protobuf-net.
    /// </summary>
    /// <remarks>
    /// Requires NuGet package: protobuf-net.
    /// Types must be annotated with [ProtoContract] / [ProtoMember] attributes.
    /// </remarks>
    public class ProtobufBinarySerializer : ISerializer
    {
        public string ContentType => "application/x-protobuf";

        public SerializationFormat Format => SerializationFormat.Protobuf;

        public string Serialize(object value)
        {
            ArgumentNullException.ThrowIfNull(value);
            using var stream = new MemoryStream();
            Serializer.Serialize(stream, value);
            return Convert.ToBase64String(stream.ToArray());
        }

        public string Serialize<T>(T value)
        {
            ArgumentNullException.ThrowIfNull(value);
            using var stream = new MemoryStream();
            Serializer.Serialize(stream, value);
            return Convert.ToBase64String(stream.ToArray());
        }

        public object? Deserialize(string data, Type type)
        {
            ArgumentNullException.ThrowIfNull(data);
            ArgumentNullException.ThrowIfNull(type);
            var bytes = Convert.FromBase64String(data);
            using var stream = new MemoryStream(bytes);
            return Serializer.Deserialize(type, stream);
        }

        public T? Deserialize<T>(string data)
        {
            ArgumentNullException.ThrowIfNull(data);
            var bytes = Convert.FromBase64String(data);
            using var stream = new MemoryStream(bytes);
            return Serializer.Deserialize<T>(stream);
        }

        public byte[] SerializeToBytes(object value)
        {
            ArgumentNullException.ThrowIfNull(value);
            using var stream = new MemoryStream();
            Serializer.Serialize(stream, value);
            return stream.ToArray();
        }

        public byte[] SerializeToBytes<T>(T value)
        {
            ArgumentNullException.ThrowIfNull(value);
            using var stream = new MemoryStream();
            Serializer.Serialize(stream, value);
            return stream.ToArray();
        }

        public object? DeserializeFromBytes(byte[] data, Type type)
        {
            ArgumentNullException.ThrowIfNull(data);
            ArgumentNullException.ThrowIfNull(type);
            using var stream = new MemoryStream(data);
            return Serializer.Deserialize(type, stream);
        }

        public T? DeserializeFromBytes<T>(byte[] data)
        {
            ArgumentNullException.ThrowIfNull(data);
            using var stream = new MemoryStream(data);
            return Serializer.Deserialize<T>(stream);
        }

        public void Serialize(Stream stream, object value)
        {
            ArgumentNullException.ThrowIfNull(stream);
            ArgumentNullException.ThrowIfNull(value);
            Serializer.Serialize(stream, value);
        }

        public void Serialize<T>(Stream stream, T value)
        {
            ArgumentNullException.ThrowIfNull(stream);
            ArgumentNullException.ThrowIfNull(value);
            Serializer.Serialize(stream, value);
        }

        public object? Deserialize(Stream stream, Type type)
        {
            ArgumentNullException.ThrowIfNull(stream);
            ArgumentNullException.ThrowIfNull(type);
            return Serializer.Deserialize(type, stream);
        }

        public T? Deserialize<T>(Stream stream)
        {
            ArgumentNullException.ThrowIfNull(stream);
            return Serializer.Deserialize<T>(stream);
        }

        public Task SerializeAsync(Stream stream, object value, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(stream);
            ArgumentNullException.ThrowIfNull(value);
            cancellationToken.ThrowIfCancellationRequested(); // CR-M245: observe the token (DeserializeAsync already does)
            Serializer.Serialize(stream, value);
            return Task.CompletedTask;
        }

        public Task SerializeAsync<T>(Stream stream, T value, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(stream);
            ArgumentNullException.ThrowIfNull(value);
            cancellationToken.ThrowIfCancellationRequested(); // CR-M245
            Serializer.Serialize(stream, value);
            return Task.CompletedTask;
        }

        public Task<object?> DeserializeAsync(Stream stream, Type type, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(stream);
            ArgumentNullException.ThrowIfNull(type);
            // CR-L363: protobuf-net has no native async API. Deserialize synchronously and complete a Task
            // (observing the token up front) rather than offloading pure CPU work to a thread-pool thread via
            // Task.Run — matches the SerializeAsync shape above.
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<object?>(Serializer.Deserialize(type, stream));
        }

        public Task<T?> DeserializeAsync<T>(Stream stream, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(stream);
            cancellationToken.ThrowIfCancellationRequested(); // CR-L363
            return Task.FromResult<T?>(Serializer.Deserialize<T>(stream));
        }
    }
}
