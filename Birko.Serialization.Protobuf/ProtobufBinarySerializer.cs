using System;
using System.IO;
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
    }
}
