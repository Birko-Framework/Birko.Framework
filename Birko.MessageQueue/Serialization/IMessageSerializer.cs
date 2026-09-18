using System;

namespace Birko.MessageQueue.Serialization
{
    /// <summary>
    /// Serializes and deserializes message payloads.
    /// </summary>
    public interface IMessageSerializer
    {
        /// <summary>
        /// Serializes the payload to a string.
        /// </summary>
        string Serialize(object payload);

        /// <summary>
        /// Deserializes the string back to an object of the specified type.
        /// </summary>
        object? Deserialize(string data, Type type);

        /// <summary>
        /// Deserializes the string to a typed object.
        /// </summary>
        T? Deserialize<T>(string data) where T : class;

        /// <summary>
        /// The content type this serializer produces (e.g., "application/json").
        /// </summary>
        string ContentType { get; }
    }
}
