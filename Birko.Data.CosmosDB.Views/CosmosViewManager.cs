using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Birko.Data.Views;
using Microsoft.Azure.Cosmos;

namespace Birko.Data.CosmosDB.Views;

/// <summary>
/// Azure Cosmos DB (NoSQL API) implementation of <see cref="IViewManager"/>.
/// Cosmos DB does not support native server-side views or materialized views.
/// EnsureAsync and RefreshAsync are no-ops. DropAsync deletes the named container.
/// ExistsAsync checks whether a container with the given name exists.
/// </summary>
public class CosmosViewManager : IViewManager
{
    private readonly Database _database;

    /// <summary>
    /// Initializes a new instance of the <see cref="CosmosViewManager"/> class.
    /// </summary>
    /// <param name="database">The Cosmos DB database.</param>
    public CosmosViewManager(Database database)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
    }

    /// <summary>
    /// No-op. Cosmos DB does not support server-side views.
    /// Creating a materialized view would require a change-feed-based materialization pipeline,
    /// which is beyond the scope of this store implementation. View queries are always computed
    /// on-the-fly via <see cref="CosmosViewStore{TView}"/>.
    /// </summary>
    public Task EnsureAsync(ViewDefinition definition, CancellationToken ct = default)
    {
        // Cosmos DB has no native view support. Views are computed on-the-fly by CosmosViewStore.
        // A true persistent/materialized view would require Azure Functions or change-feed processors
        // to maintain a denormalized container, which is outside the scope of this project.
        return Task.CompletedTask;
    }

    /// <summary>
    /// Drops a container by name. In the absence of native views, this deletes
    /// a container that may have been used as a materialized view target.
    /// </summary>
    public async Task DropAsync(string viewName, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(viewName))
        {
            throw new ArgumentException("View name cannot be null or empty.", nameof(viewName));
        }

        var container = _database.GetContainer(viewName);
        try
        {
            await container.DeleteContainerAsync(cancellationToken: ct).ConfigureAwait(false);
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            // Idempotent teardown: dropping a non-existent view container is a no-op, matching
            // ExistsAsync's NotFound handling (CR-L109).
        }
    }

    /// <summary>
    /// Checks whether a container with the given name exists by attempting to read its properties.
    /// </summary>
    public async Task<bool> ExistsAsync(string viewName, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(viewName))
        {
            throw new ArgumentException("View name cannot be null or empty.", nameof(viewName));
        }

        try
        {
            var container = _database.GetContainer(viewName);
            await container.ReadContainerAsync(cancellationToken: ct).ConfigureAwait(false);
            return true;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }
    }

    /// <summary>
    /// No-op. Cosmos DB does not support refreshable materialized views.
    /// View data is always computed on-the-fly.
    /// </summary>
    public Task RefreshAsync(string viewName, CancellationToken ct = default)
    {
        // Cosmos DB views are computed on-the-fly; there is no materialized state to refresh.
        return Task.CompletedTask;
    }
}
