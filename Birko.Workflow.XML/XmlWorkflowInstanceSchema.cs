using System.Threading;
using System.Threading.Tasks;
using Birko.Data.XML.Stores;
using Birko.Workflow.XML.Models;

namespace Birko.Workflow.XML
{
    public static class XmlWorkflowInstanceSchema
    {
        public static async Task EnsureCreatedAsync(Birko.Configuration.Settings settings, CancellationToken cancellationToken = default)
        {
            var store = new AsyncXmlStore<XmlWorkflowInstanceModel>();
            store.SetSettings(settings);
            await store.InitAsync(cancellationToken).ConfigureAwait(false);
        }

        public static async Task DropAsync(Birko.Configuration.Settings settings, CancellationToken cancellationToken = default)
        {
            var store = new AsyncXmlStore<XmlWorkflowInstanceModel>();
            store.SetSettings(settings);
            await store.DestroyAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
