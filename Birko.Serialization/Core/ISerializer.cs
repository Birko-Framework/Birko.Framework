using System;

namespace Birko.Serialization
{
    /// <summary>
    /// Unified serialization interface for the Birko Framework.
    /// Supports both string and byte array serialization with typed and untyped overloads.
    /// </summary>
    public interface ISerializer
    {
        /// <summary>
        /// The content type this serializer produces (e.g., "application/json", "application/x-msgpack", "application/x-protobuf").
        /// </summary>
        string ContentType { get; }

        /// <summary>
        /// The serialization format identifier.
        /// </summary>
        SerializationFormat Format { get; }

        /// <summary>
        /// Serializes the value to a string.
        /// </summary>
        string Serialize(object value);

        /// <summary>
        /// Serializes the typed value to a string.
        /// </summary>
        string Serialize<T>(T value);

        /// <summary>
        /// Deserializes the string to an object of the specified type.
        /// </summary>
        object? Deserialize(string data, Type type);

        /// <summary>
        /// Deserializes the string to a typed object.
        /// </summary>
        T? Deserialize<T>(string data);

        /// <summary>
        /// Serializes the value to a byte array.
        /// </summary>
        byte[] SerializeToBytes(object value);

        /// <summary>
        /// Serializes the typed value to a byte array.
        /// </summary>
        byte[] SerializeToBytes<T>(T value);

        /// <summary>
        /// Deserializes the byte array to an object of the specified type.
        /// </summary>
        object? DeserializeFromBytes(byte[] data, Type type);

        /// <summary>
        /// Deserializes the byte array to a typed object.
        /// </summary>
        T? DeserializeFromBytes<T>(byte[] data);
    }
}
