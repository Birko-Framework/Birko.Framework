using System;

namespace Birko.BackgroundJobs.Serialization
{
    /// <summary>
    /// Serializes and deserializes job input data.
    /// </summary>
    public interface IJobSerializer
    {
        /// <summary>
        /// Serializes the input object to a string.
        /// </summary>
        string Serialize(object input);

        /// <summary>
        /// Deserializes the string back to an object of the specified type.
        /// </summary>
        object? Deserialize(string data, Type type);
    }
}
