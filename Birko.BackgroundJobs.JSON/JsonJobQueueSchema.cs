using System.Threading;
using System.Threading.Tasks;
using Birko.BackgroundJobs.JSON.Models;
using Birko.Data.JSON.Stores;
using Birko.Data.Stores;
using Birko.Configuration;

namespace Birko.BackgroundJobs.JSON
{
    /// <summary>
    /// Utility for managing the background jobs JSON file.
    /// </summary>
    public static class JsonJobQueueSchema
    {
        /// <summary>
        /// Creates the jobs file. Called automatically by JsonJobQueue on first use.
        /// </summary>
        public static async Task EnsureCreatedAsync(Birko.Configuration.Settings settings, CancellationToken cancellationToken = default)
        {
            var store = new AsyncJsonStore<JsonJobDescriptorModel>();
            store.SetSettings(settings);
            await store.InitAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Deletes the jobs file. WARNING: This deletes all job data.
        /// </summary>
        public static async Task DropAsync(Birko.Configuration.Settings settings, CancellationToken cancellationToken = default)
        {
            var store = new AsyncJsonStore<JsonJobDescriptorModel>();
            store.SetSettings(settings);
            await store.DestroyAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
