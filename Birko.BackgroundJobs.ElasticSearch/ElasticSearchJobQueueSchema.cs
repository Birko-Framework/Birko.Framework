using System.Threading;
using System.Threading.Tasks;
using Birko.BackgroundJobs.ElasticSearch.Models;
using Birko.Data.ElasticSearch.Stores;

namespace Birko.BackgroundJobs.ElasticSearch
{
    /// <summary>
    /// Utility for managing the background jobs Elasticsearch index.
    /// </summary>
    public static class ElasticSearchJobQueueSchema
    {
        /// <summary>
        /// Optional explicit pre-creation utility for the jobs index. Not part of the runtime path:
        /// ElasticSearchJobQueue does not call it — the index is otherwise created lazily by the store's
        /// init on first CRUD operation (CR-L024).
        /// </summary>
        public static async Task EnsureCreatedAsync(Birko.Data.ElasticSearch.Stores.Settings settings, CancellationToken cancellationToken = default)
        {
            var store = new AsyncElasticSearchStore<ElasticJobDescriptorModel>();
            store.SetSettings(settings);
            await store.InitAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Drops the jobs index. WARNING: This deletes all job data.
        /// </summary>
        public static async Task DropAsync(Birko.Data.ElasticSearch.Stores.Settings settings, CancellationToken cancellationToken = default)
        {
            var store = new AsyncElasticSearchStore<ElasticJobDescriptorModel>();
            store.SetSettings(settings);
            await store.DestroyAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
