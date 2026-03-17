using System.Threading;
using System.Threading.Tasks;
using Birko.Data.ElasticSearch.Stores;
using Birko.Workflow.ElasticSearch.Models;

namespace Birko.Workflow.ElasticSearch
{
    public static class ElasticSearchWorkflowInstanceSchema
    {
        public static async Task EnsureCreatedAsync(Birko.Data.ElasticSearch.Stores.Settings settings, CancellationToken cancellationToken = default)
        {
            var store = new AsyncElasticSearchStore<ElasticWorkflowInstanceModel>();
            store.SetSettings(settings);
            await store.InitAsync(cancellationToken).ConfigureAwait(false);
        }

        public static async Task DropAsync(Birko.Data.ElasticSearch.Stores.Settings settings, CancellationToken cancellationToken = default)
        {
            var store = new AsyncElasticSearchStore<ElasticWorkflowInstanceModel>();
            store.SetSettings(settings);
            await store.DestroyAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
