using System;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Birko.Data.MongoDB.ChangeStreams
{
    /// <summary>
    /// Options for configuring a MongoDB change stream.
    /// </summary>
    public class ChangeStreamOptions
    {
        /// <summary>
        /// Gets or sets the full document option controlling what document data is returned with update events.
        /// Defaults to <see cref="ChangeStreamFullDocumentOption.UpdateLookup"/>.
        /// </summary>
        public ChangeStreamFullDocumentOption FullDocument { get; set; } = ChangeStreamFullDocumentOption.UpdateLookup;

        /// <summary>
        /// Gets or sets the maximum number of documents to return per batch.
        /// </summary>
        public int? BatchSize { get; set; }

        /// <summary>
        /// Gets or sets the maximum amount of time to wait for a new change before returning an empty batch.
        /// </summary>
        public TimeSpan? MaxAwaitTime { get; set; }

        /// <summary>
        /// Gets or sets a resume token to resume the change stream after a specific event.
        /// </summary>
        public BsonDocument? ResumeAfter { get; set; }

        /// <summary>
        /// Gets or sets a resume token to start the change stream after a specific event.
        /// Unlike <see cref="ResumeAfter"/>, this will not return the event matching the token.
        /// </summary>
        public BsonDocument? StartAfter { get; set; }
    }
}
