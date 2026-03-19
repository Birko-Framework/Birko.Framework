using System.Threading;
using System.Threading.Tasks;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.Stores;
using Birko.Data.Stores;
using Birko.Configuration;
using Birko.Workflow.SQL.Models;

namespace Birko.Workflow.SQL
{
    public static class SqlWorkflowInstanceSchema
    {
        public static async Task EnsureCreatedAsync<DB>(PasswordSettings settings, CancellationToken cancellationToken = default)
            where DB : AbstractConnector
        {
            var store = new AsyncDataBaseBulkStore<DB, WorkflowInstanceModel>();
            store.SetSettings(settings);
            await store.InitAsync(cancellationToken).ConfigureAwait(false);
        }

        public static async Task DropAsync<DB>(PasswordSettings settings, CancellationToken cancellationToken = default)
            where DB : AbstractConnector
        {
            var store = new AsyncDataBaseBulkStore<DB, WorkflowInstanceModel>();
            store.SetSettings(settings);
            await store.DestroyAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
