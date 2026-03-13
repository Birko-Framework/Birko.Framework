using System.Threading;
using System.Threading.Tasks;
using Birko.BackgroundJobs.MongoDB.Models;
using Birko.Data.MongoDB.Stores;

namespace Birko.BackgroundJobs.MongoDB
{
    /// <summary>
    /// Utility for managing the background jobs MongoDB collection.
    /// </summary>
    public static class MongoDBJobQueueSchema
    {
        /// <summary>
        /// Initializes the jobs collection. Called automatically by MongoDBJobQueue on first use.
        /// </summary>
        public static async Task EnsureCreatedAsync(Birko.Data.MongoDB.Stores.Settings settings, CancellationToken cancellationToken = default)
        {
            var store = new AsyncMongoDBStore<MongoJobDescriptorModel>();
            store.SetSettings(settings);
            await store.InitAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Drops the jobs collection. WARNING: This deletes all job data.
        /// </summary>
        public static async Task DropAsync(Birko.Data.MongoDB.Stores.Settings settings, CancellationToken cancellationToken = default)
        {
            var store = new AsyncMongoDBStore<MongoJobDescriptorModel>();
            store.SetSettings(settings);
            await store.DestroyAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
