using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Birko.BackgroundJobs
{
    /// <summary>
    /// Counts jobs per status, for queues whose storage can answer that without fetching the rows.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Separate from <see cref="IJobQueue"/> on purpose.</b> There are nine implementations of that
    /// interface across nine repositories; adding a member would break every one of them for a capability
    /// only some backends can answer efficiently. An optional interface lets a queue advertise the ability
    /// instead, and a caller test for it.
    /// </para>
    /// <para>
    /// <b>Why it is needed at all, when <c>GetByStatusAsync</c> exists.</b> That method takes a
    /// <c>limit</c> (default 100), so counting by taking the length of its result silently reports 100 for
    /// any larger backlog — a dashboard whose headline number is wrong precisely when the backlog is
    /// interesting. Fetching rows to count them is also the wrong shape regardless of the cap: the answer
    /// is one number and the storage can produce it.
    /// </para>
    /// <para>
    /// Returns every status in one call rather than taking one, because the caller for this is a summary
    /// view that wants all of them and would otherwise issue a query per status from the outside.
    /// </para>
    /// </remarks>
    public interface IJobQueueCounts
    {
        /// <summary>
        /// How many jobs are in each status. Statuses with no jobs may be absent from the result;
        /// callers should treat a missing key as zero.
        /// </summary>
        Task<IReadOnlyDictionary<JobStatus, int>> CountByStatusAsync(CancellationToken cancellationToken = default);
    }
}
