using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Birko.Serialization
{
    /// <summary>
    /// Unified serialization interface for the Birko Framework.
    /// Supports string, byte array, and stream serialization with typed and untyped overloads.
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

        /// <summary>
        /// Serializes the value to a stream.
        /// </summary>
        void Serialize(Stream stream, object value);

        /// <summary>
        /// Serializes the typed value to a stream.
        /// </summary>
        void Serialize<T>(Stream stream, T value);

        /// <summary>
        /// Deserializes the stream to an object of the specified type.
        /// </summary>
        object? Deserialize(Stream stream, Type type);

        /// <summary>
        /// Deserializes the stream to a typed object.
        /// </summary>
        T? Deserialize<T>(Stream stream);

        /// <summary>
        /// Serializes the value to a stream asynchronously.
        /// </summary>
        Task SerializeAsync(Stream stream, object value, CancellationToken cancellationToken = default);

        /// <summary>
        /// Serializes the typed value to a stream asynchronously.
        /// </summary>
        Task SerializeAsync<T>(Stream stream, T value, CancellationToken cancellationToken = default);

        /// <summary>
        /// Deserializes the stream to an object of the specified type asynchronously.
        /// </summary>
        Task<object?> DeserializeAsync(Stream stream, Type type, CancellationToken cancellationToken = default);

        /// <summary>
        /// Deserializes the stream to a typed object asynchronously.
        /// </summary>
        Task<T?> DeserializeAsync<T>(Stream stream, CancellationToken cancellationToken = default);
    }
}
