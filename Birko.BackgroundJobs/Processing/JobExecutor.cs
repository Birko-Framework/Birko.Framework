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

                // SH-L002: JobType and InputType come from storage, so the type is checked BEFORE the factory builds it.
                // The typed path invoked any type with a matching ExecuteAsync signature, implementing IJob<T> or not.
                var typedInput = descriptor.InputType != null && descriptor.SerializedInput != null;
                Type? inputType = null;
                if (typedInput)
                {
                    inputType = Type.GetType(descriptor.InputType!);
                    if (inputType == null)
                    {
                        return JobResult.Failed(stopwatch.Elapsed, $"Input type not found: {descriptor.InputType}");
                    }
                    if (!typeof(IJob<>).MakeGenericType(inputType).IsAssignableFrom(jobType))
                    {
                        return JobResult.Failed(stopwatch.Elapsed, $"Job {jobType.Name} does not implement IJob<{inputType.Name}>");
                    }
                }
                else if (!typeof(IJob).IsAssignableFrom(jobType))
                {
                    return JobResult.Failed(stopwatch.Elapsed, $"Job {jobType.Name} does not implement IJob");
                }

                var job = _jobFactory(jobType);
                var context = new JobContext(
                    descriptor.Guid,
                    descriptor.AttemptCount,
                    descriptor.EnqueuedAt,
                    descriptor.Metadata
                );

                if (typedInput)
                {
                    var input = _serializer.Deserialize(descriptor.SerializedInput!, inputType!);
                    if (input == null)
                    {
                        return JobResult.Failed(stopwatch.Elapsed, "Failed to deserialize job input");
                    }

                    // Invoke IJob<TInput>.ExecuteAsync via reflection
                    var executeMethod = typeof(IJob<>).MakeGenericType(inputType!).GetMethod("ExecuteAsync");
                    if (executeMethod == null)
                    {
                        return JobResult.Failed(stopwatch.Elapsed, $"ExecuteAsync method not found on {jobType.Name}");
                    }

                    var task = (Task?)executeMethod.Invoke(job, new[] { input, context, cancellationToken });
                    if (task == null)
                    {
                        // A matched ExecuteAsync that did not return an awaitable Task is a contract
                        // violation (IJob<TInput> requires Task). Surface it as a failure rather than
                        // falling through to Succeeded without ever running/awaiting the job (CR-L019).
                        return JobResult.Failed(stopwatch.Elapsed, $"ExecuteAsync on {jobType.Name} did not return a Task");
                    }

                    await task.ConfigureAwait(false);
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
                // SH-L003: only reflection's own wrapper is unwrapped. Every exception's inner one was taken, so a job's
                // ImportFailedException("file 3 of 40 rejected", ioEx) was reported as the bare IOException.
                var thrown = ex is System.Reflection.TargetInvocationException { InnerException: { } inner } ? inner : ex;
                return JobResult.Failed(stopwatch.Elapsed, thrown.Message, thrown);
            }
        }
    }
}
