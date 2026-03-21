using System;
using System.Collections.Generic;

namespace Birko.Data.Patterns.IndexManagement
{
    /// <summary>
    /// Provider-agnostic index definition for NoSQL stores.
    /// </summary>
    public class IndexDefinition
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
        /// Gets or sets whether the index is sparse (skips documents without the indexed field).
        /// Applicable to MongoDB. Ignored by providers that don't support sparse indexes.
        /// </summary>
        public bool Sparse { get; set; }

        /// <summary>
        /// Gets or sets the TTL expiration for documents. Applicable to MongoDB TTL indexes.
        /// Ignored by providers that don't support TTL indexes.
        /// </summary>
        public TimeSpan? ExpireAfter { get; set; }

        /// <summary>
        /// Gets or sets provider-specific properties (e.g., ES analyzer, Raven map expression).
        /// </summary>
        public IDictionary<string, object>? Properties { get; set; }
    }

    /// <summary>
    /// A single field within an index definition.
    /// Matches <see cref="Birko.Data.SQL.Tables.IndexColumn"/> convention (bool IsDescending).
    /// </summary>
    public class IndexField
    {
        /// <summary>
        /// Gets or sets the field name (dot notation supported, e.g., "Address.City").
        /// </summary>
        public string Name { get; set; } = null!;

        /// <summary>
        /// Gets or sets whether the field is sorted descending. Default is false (ascending).
        /// Matches <c>IndexColumn.IsDescending</c> and <c>IndexedField.IsDescending</c> in Birko.Data.SQL.
        /// </summary>
        public bool IsDescending { get; set; }

        /// <summary>
        /// Gets or sets the field type for specialized indexes.
        /// </summary>
        public IndexFieldType FieldType { get; set; } = IndexFieldType.Standard;

        /// <summary>
        /// Creates an ascending field.
        /// </summary>
        public static IndexField Ascending(string name) => new() { Name = name };

        /// <summary>
        /// Creates a descending field.
        /// </summary>
        public static IndexField Descending(string name) => new() { Name = name, IsDescending = true };

        /// <summary>
        /// Creates a text (full-text) field.
        /// </summary>
        public static IndexField Text(string name) => new() { Name = name, FieldType = IndexFieldType.Text };

        /// <summary>
        /// Creates a hashed field.
        /// </summary>
        public static IndexField Hashed(string name) => new() { Name = name, FieldType = IndexFieldType.Hashed };

        /// <summary>
        /// Creates a geo-spatial 2dsphere field.
        /// </summary>
        public static IndexField Geo2dSphere(string name) => new() { Name = name, FieldType = IndexFieldType.Geo2dSphere };
    }

    /// <summary>
    /// Index field type for specialized indexes.
    /// </summary>
    public enum IndexFieldType
    {
        /// <summary>Standard B-tree index.</summary>
        Standard,
        /// <summary>Full-text search index.</summary>
        Text,
        /// <summary>Geospatial 2d index.</summary>
        Geo2d,
        /// <summary>Geospatial 2dsphere index.</summary>
        Geo2dSphere,
        /// <summary>Hashed index (for sharding/equality queries).</summary>
        Hashed
    }
}
