using System;
using System.Threading;
using System.Threading.Tasks;
using Raven.Client.Documents;
using Raven.Client.Documents.Operations;
using Raven.Client.Exceptions;
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
    /// <para>
    /// TASK-1004: only those two refusals are retried (<see cref="IsConcurrentCreateRefusal"/>). Measured over 10 rounds of
    /// 8 racing creates: 64 <see cref="DatabaseDisabledException"/>, 2 <see cref="ConcurrencyException"/> ("already
    /// exists"), nothing else. Any other failure, such as an unreachable server, a version mismatch or a refused
    /// permission, surfaces at once; it used to be retried for the whole 30 s.
    /// </para>
    /// <para>
    /// TASK-1005: <see cref="EnsureReadyAsync"/> is the async store's walk. It sends asynchronously and waits with
    /// <see cref="Task.Delay(TimeSpan, CancellationToken)"/>, so it holds no thread: the async store ran the sync walk, which
    /// slept on the caller's thread for up to 30 s before its first call even returned a task (measured). The token is
    /// whatever init runs under, and that is <see cref="CancellationToken.None"/>: one-time setup is shared, so no single
    /// caller's token may stop it (TASK-324). The client's request executor also retries a disabled database for about
    /// 30 s inside <c>SendAsync</c> (measured), asynchronously.
    /// </para>
    /// </remarks>
    internal static class RavenDatabaseBootstrap
    {
        /// <summary>How long a store waits for a database other clients are creating at the same time.</summary>
        public static readonly TimeSpan ConcurrentCreateWait = TimeSpan.FromSeconds(30);

        /// <summary>The refusals a racing create gets from the server, and the only ones worth sending the create again for.</summary>
        internal static bool IsConcurrentCreateRefusal(Exception ex)
            => ex is DatabaseDisabledException || ex is ConcurrencyException;

        public static void EnsureReady(IDocumentStore store, string dbName)
        {
            var wait = new Wait();
            while (!Exists(store, dbName))
            {
                try
                {
                    store.Maintenance.Server.Send(new CreateDatabaseOperation(new DatabaseRecord(dbName)));
                    break;
                }
                catch (Exception ex) when (IsConcurrentCreateRefusal(ex) && wait.Open)
                {
                    Thread.Sleep(wait.Next());
                }
            }
            while (true)
            {
                try
                {
                    store.Maintenance.ForDatabase(dbName).Send(new GetStatisticsOperation());
                    return;
                }
                catch (DatabaseDisabledException) when (wait.Open)
                {
                    Thread.Sleep(wait.Next());
                }
            }
        }

        public static async Task EnsureReadyAsync(IDocumentStore store, string dbName, CancellationToken ct)
        {
            var wait = new Wait();
            while (!await ExistsAsync(store, dbName, ct).ConfigureAwait(false))
            {
                try
                {
                    await store.Maintenance.Server.SendAsync(new CreateDatabaseOperation(new DatabaseRecord(dbName)), ct).ConfigureAwait(false);
                    break;
                }
                catch (Exception ex) when (IsConcurrentCreateRefusal(ex) && wait.Open)
                {
                    await Task.Delay(wait.Next(), ct).ConfigureAwait(false);
                }
            }
            while (true)
            {
                try
                {
                    await store.Maintenance.ForDatabase(dbName).SendAsync(new GetStatisticsOperation(), ct).ConfigureAwait(false);
                    return;
                }
                catch (DatabaseDisabledException) when (wait.Open)
                {
                    await Task.Delay(wait.Next(), ct).ConfigureAwait(false);
                }
            }
        }

        public static bool Exists(IDocumentStore store, string dbName)
            => store.Maintenance.Server.Send(new GetDatabaseRecordOperation(dbName)) != null;

        public static async Task<bool> ExistsAsync(IDocumentStore store, string dbName, CancellationToken ct)
            => await store.Maintenance.Server.SendAsync(new GetDatabaseRecordOperation(dbName), ct).ConfigureAwait(false) != null;

        /// <summary>The deadline and the spacing of the retries: 50 ms, doubled while under 1 s, plus up to 50 ms of jitter.</summary>
        private sealed class Wait
        {
            private readonly DateTime _until = DateTime.UtcNow + ConcurrentCreateWait;
            private TimeSpan _pause = TimeSpan.FromMilliseconds(50);

            public bool Open => DateTime.UtcNow < _until;

            public TimeSpan Next()
            {
                var next = _pause + TimeSpan.FromMilliseconds(System.Random.Shared.Next(50));
                if (_pause < TimeSpan.FromSeconds(1)) _pause *= 2;
                return next;
            }
        }
    }
}
