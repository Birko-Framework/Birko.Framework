using System;
using Raven.Client.Documents;
using Raven.Client.Documents.Operations;
using Raven.Client.Exceptions.Database;
using Raven.Client.ServerWide;
using Raven.Client.ServerWide.Operations;

namespace Birko.Data.RavenDB.Stores
{
    /// <summary>
    /// Brings a store's database to "answers requests", shared by <see cref="RavenDBStore{T}"/> and
    /// <see cref="AsyncRavenDBStore{T}"/> (TASK-996).
    /// </summary>
    /// <remarks>
    /// Several clients may start on a new database at once (app instances starting together). Measured on a live server:
    /// <list type="bullet">
    /// <item>the server refuses every create but one, as "already exists" or as "unloaded and locked" while another create
    /// runs, and with eight clients at once it can refuse every create, so none succeeds;</item>
    /// <item>a database that is listed can still be loading: the first request then fails with
    /// <see cref="DatabaseDisabledException"/> ("unloaded and locked").</item>
    /// </list>
    /// So a refused create is retried, briefly spaced, until the database is listed, and then a request is retried while it
    /// answers <see cref="DatabaseDisabledException"/>. Both stop at <see cref="ConcurrentCreateWait"/>, after which the last
    /// failure is thrown. A database that exists and is loaded costs one listing and one statistics request.
    /// </remarks>
    internal static class RavenDatabaseBootstrap
    {
        /// <summary>How long a store waits for a database other clients are creating at the same time.</summary>
        public static readonly TimeSpan ConcurrentCreateWait = TimeSpan.FromSeconds(30);

        public static void EnsureReady(IDocumentStore store, string dbName)
        {
            var until = DateTime.UtcNow + ConcurrentCreateWait;
            var pause = TimeSpan.FromMilliseconds(50);
            while (!Exists(store, dbName))
            {
                try
                {
                    store.Maintenance.Server.Send(new CreateDatabaseOperation(new DatabaseRecord(dbName)));
                    break;
                }
                catch (Exception) when (DateTime.UtcNow < until)
                {
                    Pause(ref pause);
                }
            }
            while (true)
            {
                try
                {
                    store.Maintenance.ForDatabase(dbName).Send(new GetStatisticsOperation());
                    return;
                }
                catch (DatabaseDisabledException) when (DateTime.UtcNow < until)
                {
                    Pause(ref pause);
                }
            }
        }

        public static bool Exists(IDocumentStore store, string dbName)
            => store.Maintenance.Server.Send(new GetDatabaseRecordOperation(dbName)) != null;

        private static void Pause(ref TimeSpan pause)
        {
            System.Threading.Thread.Sleep(pause + TimeSpan.FromMilliseconds(System.Random.Shared.Next(50)));
            if (pause < TimeSpan.FromSeconds(1)) pause *= 2;
        }
    }
}
