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
        /// Creates the jobs index. Called automatically by ElasticSearchJobQueue on first use.
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
