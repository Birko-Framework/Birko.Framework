using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Raven.Client.Documents;
using Raven.Client.Documents.Operations.Indexes;
using Raven.Client.Documents.Session;

namespace Birko.Data.RavenDB.Stores
{
    /// <summary>
    /// TASK-828 (the user's decision): after a store's <c>SaveChanges</c>, wait for the indexes to include what it wrote.
    /// RavenDB answers a query from an index that may not have caught up, so a filtered query straight after a write could
    /// miss it (measured under suite load: an update applied to all 6 documents, a query on the value it wrote returned 5).
    /// With the wait a store reads its own writes, as every other provider does. The cost is on the write.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The wait is a separate request, after the save (TASK-999, the user's decision).</b> It used to be the server's own
    /// wait inside the save request (<c>WaitForIndexesAfterSaveChanges</c>). Under the full suite's load the server
    /// sometimes cancelled that wait after it had committed the batch, and the client then reported the applied write as
    /// failed (2 of 4 full runs; a 2 200-document delete). Now <see cref="SaveChanges"/> / <see cref="SaveChangesAsync"/>
    /// send the save on its own, so its result is exactly the write's, and then wait:
    /// </para>
    /// <list type="number">
    /// <item>one request for the index statistics;</item>
    /// <item>for each index that is stale and covers the saved collection (any collection when none is given), an empty
    /// query that waits for it to be non-stale. Its cutoff is the moment of the query, after the save.</item>
    /// </list>
    /// <para>
    /// The wait never throws (TASK-828's rule): the write is applied by then, and reporting it as failed invites a retry that
    /// repeats it. A timeout or a failed wait returns, and a query may briefly miss the write, as before.
    /// </para>
    /// </remarks>
    public static class RavenIndexWait
    {
        /// <summary>How long a save waits for the indexes before returning anyway.</summary>
        public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

        /// <summary>Returns the session unchanged; kept so every session the stores open goes through one place.</summary>
        public static IDocumentSession Apply(IDocumentSession session) => session;

        /// <summary>Returns the session unchanged; kept so every session the stores open goes through one place.</summary>
        public static IAsyncDocumentSession Apply(IAsyncDocumentSession session) => session;

        /// <summary>Saves (refusals that applied nothing are sent again, <see cref="RavenQueryRetry"/>), then waits for the indexes.</summary>
        public static async Task SaveChangesAsync(IAsyncDocumentSession session, string? collection, CancellationToken ct)
        {
            await RavenQueryRetry.RunAsync(() => session.SaveChangesAsync(ct)).ConfigureAwait(false);
            await WaitAsync(session.Advanced.DocumentStore, DatabaseOf(session), collection).ConfigureAwait(false);
        }

        /// <summary>Saves (refusals that applied nothing are sent again, <see cref="RavenQueryRetry"/>), then waits for the indexes.</summary>
        public static void SaveChanges(IDocumentSession session, string? collection)
        {
            RavenQueryRetry.Run(() => session.SaveChanges());
            WaitAsync(session.Advanced.DocumentStore, DatabaseOf(session), collection).GetAwaiter().GetResult();
        }

        private static string DatabaseOf(object session)
            => (session as InMemoryDocumentSessionOperations)?.DatabaseName
               ?? throw new InvalidOperationException("A RavenDB session that does not name its database.");

        // Never throws, and is not cancellable: the write it follows has happened.
        private static async Task WaitAsync(IDocumentStore store, string database, string? collection)
        {
            try
            {
                var until = DateTime.UtcNow + Timeout;
                var stats = await store.Maintenance.ForDatabase(database).SendAsync(new GetIndexesStatisticsOperation()).ConfigureAwait(false);
                var stale = stats.Where(i => i.IsStale
                        && (collection == null || i.Collections == null
                            || i.Collections.Keys.Any(c => string.Equals(c, collection, StringComparison.OrdinalIgnoreCase))))
                    .Select(i => i.Name)
                    .ToList();
                if (stale.Count == 0) return;
                using var session = store.OpenAsyncSession(database);
                foreach (var index in stale)
                {
                    var left = until - DateTime.UtcNow;
                    if (left <= TimeSpan.Zero) return;
                    try
                    {
                        await session.Advanced.AsyncRawQuery<object>($"from index '{index.Replace("'", "\\'")}' limit 0")
                            .WaitForNonStaleResults(left).ToListAsync().ConfigureAwait(false);
                    }
                    catch (Exception)
                    {
                        // a timeout, or an index replaced meanwhile: the next one is still worth waiting for
                    }
                }
            }
            catch (Exception)
            {
                // the write is applied; a failed wait only means a query may briefly miss it (TASK-828)
            }
        }
    }
}
