using System;
using System.Threading;
using System.Threading.Tasks;

namespace Birko.BackgroundJobs
{
    /// <summary>
    /// Represents a background job that can be executed asynchronously.
    /// </summary>
    public interface IJob
    {
        /// <summary>
        /// Executes the job.
        /// </summary>
        Task ExecuteAsync(JobContext context, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Represents a background job with typed input data.
    /// </summary>
    public interface IJob<TInput> where TInput : class
    {
        /// <summary>
        /// Executes the job with the given input.
        /// </summary>
        Task ExecuteAsync(TInput input, JobContext context, CancellationToken cancellationToken = default);
    }
}
