using System;
using System.Threading;
using System.Threading.Tasks;

namespace Birko.BackgroundJobs
{
    /// <summary>
    /// Resolves and executes job instances from job descriptors.
    /// </summary>
    public interface IJobExecutor
    {
        /// <summary>
        /// Executes the job described by the given descriptor.
        /// </summary>
        Task<JobResult> ExecuteAsync(JobDescriptor descriptor, CancellationToken cancellationToken = default);
    }
}
