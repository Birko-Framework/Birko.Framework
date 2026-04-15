using System.Threading;
using System.Threading.Tasks;
using Birko.BackgroundJobs.XML.Models;
using Birko.Data.XML.Stores;
using Birko.Configuration;

namespace Birko.BackgroundJobs.XML
{
    /// <summary>
    /// Utility for managing the background jobs XML file.
    /// </summary>
    public static class XmlJobQueueSchema
    {
        /// <summary>
        /// Creates the jobs file. Called automatically by XmlJobQueue on first use.
        /// </summary>
        public static async Task EnsureCreatedAsync(Birko.Configuration.Settings settings, CancellationToken cancellationToken = default)
        {
            var store = new AsyncXmlStore<XmlJobDescriptorModel>();
            store.SetSettings(settings);
            await store.InitAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Deletes the jobs file. WARNING: This deletes all job data.
        /// </summary>
        public static async Task DropAsync(Birko.Configuration.Settings settings, CancellationToken cancellationToken = default)
        {
            var store = new AsyncXmlStore<XmlJobDescriptorModel>();
            store.SetSettings(settings);
            await store.DestroyAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
