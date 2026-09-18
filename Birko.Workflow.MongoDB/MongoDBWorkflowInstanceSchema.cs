using System.Threading;
using System.Threading.Tasks;
using Birko.Data.MongoDB.Stores;
using Birko.Workflow.MongoDB.Models;

namespace Birko.Workflow.MongoDB
{
    public static class MongoDBWorkflowInstanceSchema
    {
        /// <summary>
        /// Provided for cross-backend API symmetry with the other workflow schema helpers.
        /// </summary>
        /// <remarks>
        /// CR-L412: MongoDB creates collections lazily on first write and this model declares no indexes,
        /// so <see cref="AsyncMongoDBStore{T}"/>.InitCoreAsync is an intentional no-op — this call touches
        /// the store/client to validate settings but does NOT provision any schema or indexes (unlike the
        /// SQL/relational backends). It is safe and idempotent; do not read it as "indexes are created".
        /// </remarks>
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
