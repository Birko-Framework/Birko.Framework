using System;

namespace Birko.Data.Exceptions
{
    /// <summary>
    /// Thrown when a filtered write lost a race: documents it matched changed between the backend's snapshot and its
    /// write, and the backend refused those documents rather than overwrite a newer version.
    /// </summary>
    /// <remarks>
    /// <para>
    /// TASK-502. Elasticsearch's <c>update_by_query</c> / <c>delete_by_query</c> run with <c>conflicts: abort</c>.
    /// Measured on 7.17.25 with three concurrent increments over 3,000 documents: each answered 409 having updated
    /// ~330, conflicted on ~670 and never reached the rest, and the store returned normally. So ~90% of the rows
    /// silently did not change (rule 22).
    /// </para>
    /// <para>
    /// <b>The write is partial when this is thrown</b>: <see cref="Applied"/> documents were written before the abort.
    /// A caller retrying an increment must account for those, which is why this is surfaced rather than retried here.
    /// A caller for whom a conflict means "someone else got there first" (a job claim) catches this type and moves on.
    /// </para>
    /// </remarks>
    public class StoreConcurrencyException : StoreException
    {
        public StoreConcurrencyException(string message, long applied, long conflicts, Exception? innerException = null)
            : base(message, innerException)
        {
            Applied = applied;
            Conflicts = conflicts;
        }

        /// <summary>Documents the write did apply before it stopped.</summary>
        public long Applied { get; }

        /// <summary>Documents refused because they changed underneath the write.</summary>
        public long Conflicts { get; }
    }
}
