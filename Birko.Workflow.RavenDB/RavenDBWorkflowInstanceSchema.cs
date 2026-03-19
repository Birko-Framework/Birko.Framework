using System.Threading;
using System.Threading.Tasks;
using Birko.Data.RavenDB.Stores;
using Birko.Data.Stores;
using Birko.Configuration;
using Birko.Workflow.RavenDB.Models;

namespace Birko.Workflow.RavenDB
{
    public static class RavenDBWorkflowInstanceSchema
    {
        public static async Task EnsureCreatedAsync(RemoteSettings settings, CancellationToken cancellationToken = default)
        {
            var store = new AsyncRavenDBStore<RavenWorkflowInstanceModel>();
            store.SetSettings(settings);
            await store.InitAsync(cancellationToken).ConfigureAwait(false);
        }

        public static async Task DropAsync(RemoteSettings settings, CancellationToken cancellationToken = default)
        {
            var store = new AsyncRavenDBStore<RavenWorkflowInstanceModel>();
            store.SetSettings(settings);
            await store.DestroyAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
