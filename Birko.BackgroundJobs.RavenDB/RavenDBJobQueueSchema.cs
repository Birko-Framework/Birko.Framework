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
        /// Optionally pre-initializes the jobs database. Not part of the runtime path: RavenDBJobQueue
        /// does not call it — the base store otherwise lazy-initializes on first CRUD operation (CR-L030).
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
