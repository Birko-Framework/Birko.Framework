using System;
using MongoDB.Bson;

namespace Birko.Data.MongoDB.ChangeStreams
{
    /// <summary>
    /// The type of operation that caused a change stream event.
    /// </summary>
    public enum ChangeStreamOperationType
    {
        /// <summary>A new document was inserted.</summary>
        Insert,
        /// <summary>An existing document was updated.</summary>
        Update,
        /// <summary>An existing document was replaced.</summary>
        Replace,
        /// <summary>A document was deleted.</summary>
        Delete,
        /// <summary>The change stream was invalidated.</summary>
        Invalidate,
        /// <summary>The collection was dropped.</summary>
        Drop
    }

    /// <summary>
    /// Represents a single event from a MongoDB change stream.
    /// </summary>
    /// <typeparam name="T">The document type being watched.</typeparam>
    public class ChangeStreamEvent<T> where T : class
    {
        /// <summary>
        /// Gets the type of operation that produced this event.
        /// </summary>
        public ChangeStreamOperationType OperationType { get; set; }

        /// <summary>
        /// Gets the full document after the change (available for insert, update with lookup, replace).
        /// </summary>
        public T? FullDocument { get; set; }

        /// <summary>
        /// Gets the unique identifier of the affected document.
        /// </summary>
        public Guid? DocumentKey { get; set; }

        /// <summary>
        /// Gets the cluster time at which the event occurred.
        /// </summary>
        public BsonTimestamp? ClusterTime { get; set; }

        /// <summary>
        /// Gets the resume token that can be used to resume the change stream from this point.
        /// </summary>
        public BsonDocument? ResumeToken { get; set; }
    }
}
