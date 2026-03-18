using System;
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
    }
}
