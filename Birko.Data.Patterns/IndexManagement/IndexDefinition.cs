using System;
using System.Collections.Generic;
using System.Linq;

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

        /// <summary>
        /// Fields that must hold a value for a document to be indexed: a partial index (TASK-636).
        /// </summary>
        /// <remarks>
        /// The two closed polarities SQL's <c>[IndexedField(WhereNotNull = …, WhereNull = …)]</c> declares (rule 58), never a
        /// filter document, which would be caller text reaching the server (rule 45). <c>IDefaultMarker</c>'s "at most one
        /// default" is <c>Unique</c> over <c>DefaultMarker</c> with <c>WhereNotNull = { "DefaultMarker" }</c>. MongoDB renders
        /// it as a <c>$type</c> over every non-null type, because Birko stores nulls explicitly and a partial filter takes no
        /// <c>$ne</c>; SQL as <c>WHERE … IS NOT NULL</c>. RavenDB, Cosmos DB and ElasticSearch have no partial index and refuse it.
        /// </remarks>
        public IReadOnlyList<string> WhereNotNull { get; set; } = Array.Empty<string>();

        /// <summary>Fields that must be null (or absent) for a document to be indexed, e.g. <c>DeletedAt</c> (TASK-636).</summary>
        public IReadOnlyList<string> WhereNull { get; set; } = Array.Empty<string>();

        /// <summary>Whether the definition is a partial index.</summary>
        public bool HasPredicates => WhereNotNull.Count > 0 || WhereNull.Count > 0;

        /// <summary>
        /// Refuses a predicate that cannot mean anything: an empty field name, or one field required both null and not null.
        /// </summary>
        /// <exception cref="ArgumentException">The predicate lists contradict themselves.</exception>
        public void RequireCoherentPredicates()
        {
            if (WhereNotNull.Concat(WhereNull).Any(string.IsNullOrWhiteSpace))
                throw new ArgumentException($"Index '{Name}' names an empty field in WhereNotNull or WhereNull.");
            var both = WhereNotNull.Intersect(WhereNull, StringComparer.Ordinal).FirstOrDefault();
            if (both != null)
                throw new ArgumentException($"Index '{Name}': '{both}' is in both WhereNotNull and WhereNull, so no document is indexed.");
        }

        /// <summary>
        /// Refuses a partial index on a provider that has none (rule 41), naming the provider.
        /// </summary>
        /// <exception cref="NotSupportedException">The definition declares a predicate.</exception>
        public void RefusePredicates(string provider)
        {
            if (HasPredicates)
                throw new NotSupportedException(
                    $"{provider} has no partial index; '{Name}' cannot be created with WhereNotNull or WhereNull. Enforce the rule in the application, or keep this index off {provider}.");
        }
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
