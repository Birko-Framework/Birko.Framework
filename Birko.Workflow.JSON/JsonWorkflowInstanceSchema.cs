using System.Threading;
using System.Threading.Tasks;
using Birko.Data.JSON.Stores;
using Birko.Workflow.JSON.Models;

namespace Birko.Workflow.JSON
{
    public static class JsonWorkflowInstanceSchema
    {
        public static async Task EnsureCreatedAsync(Birko.Configuration.Settings settings, CancellationToken cancellationToken = default)
        {
            var store = new AsyncJsonStore<JsonWorkflowInstanceModel>();
            store.SetSettings(settings);
            await store.InitAsync(cancellationToken).ConfigureAwait(false);
        }

        public static async Task DropAsync(Birko.Configuration.Settings settings, CancellationToken cancellationToken = default)
        {
            var store = new AsyncJsonStore<JsonWorkflowInstanceModel>();
            store.SetSettings(settings);
            await store.DestroyAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
