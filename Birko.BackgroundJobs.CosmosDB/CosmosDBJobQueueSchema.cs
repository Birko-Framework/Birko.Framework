using Birko.BackgroundJobs.CosmosDB.Models;
using Birko.Data.CosmosDB.Stores;
using System.Threading;
using System.Threading.Tasks;

namespace Birko.BackgroundJobs.CosmosDB;

/// <summary>
/// Static utility for creating and dropping the Cosmos DB job queue container.
/// </summary>
public static class CosmosDBJobQueueSchema
{
    public static async Task EnsureCreatedAsync(Settings settings, CancellationToken ct = default)
    {
        var store = new AsyncCosmosDBStore<CosmosJobDescriptorModel>();
        store.SetSettings(settings);
        await store.InitAsync(ct).ConfigureAwait(false);
    }

    public static async Task DropAsync(Settings settings, CancellationToken ct = default)
    {
        var store = new AsyncCosmosDBStore<CosmosJobDescriptorModel>();
        store.SetSettings(settings);
        await store.DestroyAsync(ct).ConfigureAwait(false);
    }
}
