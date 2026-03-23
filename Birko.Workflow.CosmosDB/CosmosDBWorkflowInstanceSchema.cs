using Birko.Workflow.CosmosDB.Models;
using Birko.Data.CosmosDB.Stores;
using Birko.Configuration;
using System.Threading;
using System.Threading.Tasks;

namespace Birko.Workflow.CosmosDB;

/// <summary>
/// Static utility for creating and dropping the Cosmos DB workflow instance container.
/// </summary>
public static class CosmosDBWorkflowInstanceSchema
{
    public static async Task EnsureCreatedAsync(RemoteSettings settings, CancellationToken ct = default)
    {
        var store = new AsyncCosmosDBStore<CosmosWorkflowInstanceModel>();
        store.SetSettings(settings);
        await store.InitAsync(ct).ConfigureAwait(false);
    }

    public static async Task DropAsync(RemoteSettings settings, CancellationToken ct = default)
    {
        var store = new AsyncCosmosDBStore<CosmosWorkflowInstanceModel>();
        store.SetSettings(settings);
        await store.DestroyAsync(ct).ConfigureAwait(false);
    }
}
