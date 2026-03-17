using System.Threading;
using System.Threading.Tasks;
using Birko.Data.MongoDB.Stores;
using Birko.Workflow.MongoDB.Models;

namespace Birko.Workflow.MongoDB
{
    public static class MongoDBWorkflowInstanceSchema
    {
        public static async Task EnsureCreatedAsync(Birko.Data.MongoDB.Stores.Settings settings, CancellationToken cancellationToken = default)
        {
            var store = new AsyncMongoDBStore<MongoWorkflowInstanceModel>();
            store.SetSettings(settings);
            await store.InitAsync(cancellationToken).ConfigureAwait(false);
        }

        public static async Task DropAsync(Birko.Data.MongoDB.Stores.Settings settings, CancellationToken cancellationToken = default)
        {
            var store = new AsyncMongoDBStore<MongoWorkflowInstanceModel>();
            store.SetSettings(settings);
            await store.DestroyAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
