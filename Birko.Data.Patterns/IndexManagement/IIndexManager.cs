using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Birko.Data.Patterns.IndexManagement
{
    /// <summary>
    /// Provider-agnostic interface for managing NoSQL indexes.
    /// Each provider interprets "scope" differently:
    /// <list type="bullet">
    ///   <item>MongoDB — scope is the collection name (required)</item>
    ///   <item>RavenDB — scope is ignored; indexes are database-wide</item>
    ///   <item>ElasticSearch — scope is the index (container) name</item>
    /// </list>
    /// </summary>
    public interface IIndexManager
    {
        /// <summary>
        /// Checks whether an index exists.
        /// </summary>
        /// <param name="indexName">The index name.</param>
        /// <param name="scope">Provider-specific scope (collection name for MongoDB, index name for ES). Null for RavenDB.</param>
        /// <param name="ct">Cancellation token.</param>
        Task<bool> ExistsAsync(string indexName, string? scope = null, CancellationToken ct = default);

        /// <summary>
        /// Creates an index from a portable definition.
        /// </summary>
        /// <param name="definition">The index definition.</param>
        /// <param name="scope">Provider-specific scope.</param>
        /// <param name="ct">Cancellation token.</param>
        Task CreateAsync(IndexDefinition definition, string? scope = null, CancellationToken ct = default);

        /// <summary>
        /// Drops an index by name.
        /// </summary>
        /// <param name="indexName">The index name.</param>
        /// <param name="scope">Provider-specific scope.</param>
        /// <param name="ct">Cancellation token.</param>
        Task DropAsync(string indexName, string? scope = null, CancellationToken ct = default);

        /// <summary>
        /// Lists all indexes in the given scope.
        /// </summary>
        /// <param name="scope">Provider-specific scope.</param>
        /// <param name="ct">Cancellation token.</param>
        Task<IReadOnlyList<IndexInfo>> ListAsync(string? scope = null, CancellationToken ct = default);

        /// <summary>
        /// Gets detailed information about a single index.
        /// </summary>
        /// <param name="indexName">The index name.</param>
        /// <param name="scope">Provider-specific scope.</param>
        /// <param name="ct">Cancellation token.</param>
        Task<IndexInfo?> GetInfoAsync(string indexName, string? scope = null, CancellationToken ct = default);
    }
}
