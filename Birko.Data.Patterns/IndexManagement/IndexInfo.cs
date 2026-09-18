using System;
using System.Collections.Generic;

namespace Birko.Data.Patterns.IndexManagement
{
    /// <summary>
    /// Provider-agnostic information about an existing index.
    /// </summary>
    public class IndexInfo
    {
        /// <summary>
        /// Gets or sets the index name.
        /// </summary>
        public string Name { get; set; } = null!;

        /// <summary>
        /// Gets or sets the indexed fields.
        /// </summary>
        public IReadOnlyList<IndexField> Fields { get; set; } = Array.Empty<IndexField>();

        /// <summary>
        /// Gets or sets whether the index enforces uniqueness.
        /// </summary>
        public bool Unique { get; set; }

        /// <summary>
        /// Gets or sets whether the index is sparse.
        /// </summary>
        public bool Sparse { get; set; }

        /// <summary>
        /// Gets or sets the TTL expiration, if this is a TTL index.
        /// </summary>
        public TimeSpan? ExpireAfter { get; set; }

        /// <summary>
        /// Gets or sets the total size of the index in bytes (-1 if not available).
        /// </summary>
        public long SizeInBytes { get; set; } = -1;

        /// <summary>
        /// Gets or sets the index state (e.g., "ready", "building", "stale", "open", "closed").
        /// </summary>
        public string State { get; set; } = "ready";

        /// <summary>
        /// Gets or sets provider-specific properties.
        /// </summary>
        public IDictionary<string, object> Properties { get; set; } = new Dictionary<string, object>();
    }
}
