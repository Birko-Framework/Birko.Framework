using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Birko.BackgroundJobs.Serialization;

namespace Birko.BackgroundJobs.Processing
{
    /// <summary>
    /// Default job executor that resolves job types and invokes them.
    /// Uses a factory function to create job instances (compatible with DI).
    /// </summary>
    public class JobExecutor : IJobExecutor
    {
        private readonly Func<Type, object> _jobFactory;
        private readonly IJobSerializer _serializer;

        /// <summary>
        /// Creates a new JobExecutor.
        /// </summary>
        /// <param name="jobFactory">Factory function that creates job instances by type (e.g., serviceProvider.GetRequiredService).</param>
        /// <param name="serializer">Serializer for job input data.</param>
        public JobExecutor(Func<Type, object> jobFactory, IJobSerializer? serializer = null)
        {
            _jobFactory = jobFactory ?? throw new ArgumentNullException(nameof(jobFactory));
            _serializer = serializer ?? new JsonJobSerializer();
        }

        public async Task<JobResult> ExecuteAsync(JobDescriptor descriptor, CancellationToken cancellationToken = default)
        {
            var stopwatch = Stopwatch.StartNew();

            try
            {
                var jobType = Type.GetType(descriptor.JobType);
                if (jobType == null)
                {
                    return JobResult.Failed(stopwatch.Elapsed, $"Job type not found: {descriptor.JobType}");
                }

                var job = _jobFactory(jobType);
                var context = new JobContext(
                    descriptor.Id,
                    descriptor.AttemptCount,
                    descriptor.EnqueuedAt,
                    descriptor.Metadata
                );

                if (descriptor.InputType != null && descriptor.SerializedInput != null)
                {
                    var inputType = Type.GetType(descriptor.InputType);
                    if (inputType == null)
                    {
                        return JobResult.Failed(stopwatch.Elapsed, $"Input type not found: {descriptor.InputType}");
                    }

                    var input = _serializer.Deserialize(descriptor.SerializedInput, inputType);
                    if (input == null)
                    {
                        return JobResult.Failed(stopwatch.Elapsed, "Failed to deserialize job input");
                    }

                    // Invoke IJob<TInput>.ExecuteAsync via reflection
                    var executeMethod = jobType.GetMethod("ExecuteAsync", new[] { inputType, typeof(JobContext), typeof(CancellationToken) });
                    if (executeMethod == null)
                    {
                        return JobResult.Failed(stopwatch.Elapsed, $"ExecuteAsync method not found on {jobType.Name}");
                    }

                    var task = (Task?)executeMethod.Invoke(job, new[] { input, context, cancellationToken });
                    if (task != null)
                    {
                        await task.ConfigureAwait(false);
                    }
                }
                else
                {
                    if (job is IJob simpleJob)
                    {
                        await simpleJob.ExecuteAsync(context, cancellationToken).ConfigureAwait(false);
                    }
                    else
                    {
                        return JobResult.Failed(stopwatch.Elapsed, $"Job {jobType.Name} does not implement IJob");
                    }
                }

                stopwatch.Stop();
                return JobResult.Succeeded(stopwatch.Elapsed);
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                var innerEx = ex.InnerException ?? ex;
                return JobResult.Failed(stopwatch.Elapsed, innerEx.Message, innerEx);
            }
        }
    }
}
