using System;
using Raven.Client.Documents.Session;

namespace Birko.Data.RavenDB.Stores
{
    /// <summary>
    /// TASK-828 (the user's decision): every session the stores and the unit of work open waits, after
    /// <c>SaveChanges</c>, for the indexes to include what it wrote. RavenDB answers a query from an index that may not
    /// have caught up, so a filtered query straight after a write could miss it (measured under suite load: an update
    /// applied to all 6 documents, a query on the value it wrote returned 5). With the wait a store reads its own writes,
    /// as every other provider does. The cost is on the write: it returns once the indexes are current.
    /// </summary>
    /// <remarks>
    /// A timeout does not throw. By then the write is applied, and reporting an applied write as failed invites a retry
    /// that repeats it; the write returns, and a query may still briefly miss it, as before.
    /// </remarks>
    public static class RavenIndexWait
    {
        /// <summary>How long a save waits for the indexes before returning anyway.</summary>
        public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

        /// <summary>Turns the wait on for a sync session and returns it.</summary>
        public static IDocumentSession Apply(IDocumentSession session)
        {
            session.Advanced.WaitForIndexesAfterSaveChanges(Timeout, throwOnTimeout: false);
            return session;
        }

        /// <summary>Turns the wait on for an async session and returns it.</summary>
        public static IAsyncDocumentSession Apply(IAsyncDocumentSession session)
        {
            session.Advanced.WaitForIndexesAfterSaveChanges(Timeout, throwOnTimeout: false);
            return session;
        }
    }
}
