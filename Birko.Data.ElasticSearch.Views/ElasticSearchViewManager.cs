using System;
using System.Threading;
using System.Threading.Tasks;
using Birko.Data.Views;
using Nest;

namespace Birko.Data.ElasticSearch.Views;

/// <summary>
/// ElasticSearch implementation of <see cref="IViewManager"/>.
/// </summary>
/// <remarks>
/// ElasticSearch does not have native database views. Persistent views in ES would typically
/// require Transforms (X-Pack, deprecated in 8.x) or a reindexing strategy where data is
/// pre-aggregated into a destination index. This manager treats the persistent view name as
/// an index name and provides basic index lifecycle operations.
///
/// For production use of persistent views, consider:
/// - Using ES Transforms (if available) to continuously materialize aggregated data
/// - Scheduling reindex jobs via Birko.BackgroundJobs to populate the destination index
/// - Using index aliases to swap between old and new materialized data
/// </remarks>
public class ElasticSearchViewManager : IViewManager
{
    private readonly ElasticClient _connector;

    /// <summary>
    /// Initializes a new instance of the <see cref="ElasticSearchViewManager"/> class.
    /// </summary>
    /// <param name="connector">The NEST ElasticClient instance.</param>
    public ElasticSearchViewManager(ElasticClient connector)
    {
        _connector = connector ?? throw new ArgumentNullException(nameof(connector));
    }

    /// <inheritdoc />
    /// <remarks>
    /// For persistent views, ElasticSearch does not support native view creation.
    /// This method ensures the destination index exists. Data population must be handled
    /// separately (e.g., via Transforms, reindex pipelines, or background jobs).
    /// For OnTheFly views, this is a no-op.
    /// </remarks>
    public async Task EnsureAsync(ViewDefinition definition, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        if (definition == null)
        {
            throw new ArgumentNullException(nameof(definition));
        }

        if (definition.QueryMode == ViewQueryMode.OnTheFly)
        {
            return;
        }

        var indexName = ResolveIndexName(definition);
        if (string.IsNullOrEmpty(indexName))
        {
            throw new InvalidOperationException("View name is required for persistent views.");
        }

        var existsResponse = await _connector.Indices.ExistsAsync(indexName, null, ct).ConfigureAwait(false);
        if (existsResponse.Exists)
        {
            return;
        }

        // Create the destination index with dynamic mapping.
        // The index will be populated by an external process (transform, reindex, or background job).
        var createResponse = await _connector.Indices.CreateAsync(indexName, c => c, ct).ConfigureAwait(false);

        if (!createResponse.IsValid || createResponse.OriginalException != null)
        {
            throw new InvalidOperationException(
                $"Failed to create view index '{indexName}'. DebugInfo: {createResponse.DebugInformation}",
                createResponse.OriginalException);
        }
    }

    /// <inheritdoc />
    public async Task DropAsync(string viewName, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(viewName))
        {
            throw new ArgumentException("View name cannot be null or empty.", nameof(viewName));
        }

        var indexName = viewName.ToLowerInvariant();
        var response = await _connector.Indices.DeleteAsync(indexName, null, ct).ConfigureAwait(false);

        if (!response.IsValid || response.OriginalException != null)
        {
            // Ignore 404 (index not found) — drop is idempotent
            if (response.ServerError?.Status == 404)
            {
                return;
            }

            throw new InvalidOperationException(
                $"Failed to drop view index '{indexName}'. DebugInfo: {response.DebugInformation}",
                response.OriginalException);
        }
    }

    /// <inheritdoc />
    public async Task<bool> ExistsAsync(string viewName, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(viewName))
        {
            throw new ArgumentException("View name cannot be null or empty.", nameof(viewName));
        }

        var indexName = viewName.ToLowerInvariant();
        var response = await _connector.Indices.ExistsAsync(indexName, null, ct).ConfigureAwait(false);

        return response.Exists;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Refreshes the ElasticSearch index, making all recently indexed documents available for search.
    /// This is useful after populating a persistent view index via reindexing or transforms.
    /// </remarks>
    public async Task RefreshAsync(string viewName, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(viewName))
        {
            throw new ArgumentException("View name cannot be null or empty.", nameof(viewName));
        }

        var indexName = viewName.ToLowerInvariant();
        var response = await _connector.Indices.RefreshAsync(indexName, null, ct).ConfigureAwait(false);

        if (!response.IsValid || response.OriginalException != null)
        {
            throw new InvalidOperationException(
                $"Failed to refresh view index '{indexName}'. DebugInfo: {response.DebugInformation}",
                response.OriginalException);
        }
    }

    private static string ResolveIndexName(ViewDefinition definition)
    {
        if (!string.IsNullOrEmpty(definition.Name))
        {
            return definition.Name!.ToLowerInvariant();
        }

        return definition.PrimarySource.Name.ToLowerInvariant();
    }
}
