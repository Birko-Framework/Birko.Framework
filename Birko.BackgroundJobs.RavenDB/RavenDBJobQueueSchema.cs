using System.Threading;
using System.Threading.Tasks;
using Birko.BackgroundJobs.RavenDB.Models;
using Birko.Data.RavenDB.Stores;
using Birko.Data.Stores;

namespace Birko.BackgroundJobs.RavenDB
{
    /// <summary>
    /// Utility for managing the background jobs RavenDB database.
    /// </summary>
    public static class RavenDBJobQueueSchema
    {
        /// <summary>
        /// Initializes the jobs database. Called automatically by RavenDBJobQueue on first use.
        /// </summary>
        public static async Task EnsureCreatedAsync(Settings settings, CancellationToken cancellationToken = default)
        {
            var store = new AsyncRavenDBStore<RavenJobDescriptorModel>();
            store.SetSettings(settings);
            await store.InitAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Drops the jobs database. WARNING: This deletes all job data.
        /// </summary>
        public static async Task DropAsync(Settings settings, CancellationToken cancellationToken = default)
        {
            var store = new AsyncRavenDBStore<RavenJobDescriptorModel>();
            store.SetSettings(settings);
            await store.DestroyAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
