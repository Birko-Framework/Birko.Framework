using System;
using System.Data;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.Stores;
using Birko.Data.Stores;
using Birko.Configuration;
using Birko.BackgroundJobs.SQL.Models;

namespace Birko.BackgroundJobs.SQL
{
    /// <summary>
    /// Utility for verifying and managing the jobs table schema.
    /// Note: The table is auto-created by the connector's DoInit() when using SqlJobQueue.
    /// This class provides additional utilities for manual schema management.
    /// </summary>
    public static class SqlJobQueueSchema
    {
        /// <summary>
        /// Initializes the jobs table using the Birko.Data.SQL connector.
        /// This is called automatically by SqlJobQueue on first use.
        /// </summary>
        public static async Task EnsureCreatedAsync<DB>(PasswordSettings settings, CancellationToken cancellationToken = default)
            where DB : AbstractConnector
        {
            var store = new AsyncDataBaseBulkStore<DB, JobDescriptorModel>();
            store.SetSettings(settings);
            await store.InitAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Drops the jobs table using the Birko.Data.SQL connector.
        /// WARNING: This deletes all job data.
        /// </summary>
        public static async Task DropAsync<DB>(PasswordSettings settings, CancellationToken cancellationToken = default)
            where DB : AbstractConnector
        {
            var store = new AsyncDataBaseBulkStore<DB, JobDescriptorModel>();
            store.SetSettings(settings);
            await store.DestroyAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
