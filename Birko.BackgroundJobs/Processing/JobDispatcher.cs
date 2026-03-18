using System;
using System.Threading;
using System.Threading.Tasks;
using Birko.BackgroundJobs.Serialization;
using Birko.Time;

namespace Birko.BackgroundJobs.Processing
{
    /// <summary>
    /// High-level API for enqueuing jobs with a fluent interface.
    /// </summary>
    public class JobDispatcher
    {
        private readonly IJobQueue _queue;
        private readonly IJobSerializer _serializer;
        private readonly IDateTimeProvider _clock;

        public JobDispatcher(IJobQueue queue, IDateTimeProvider clock, IJobSerializer? serializer = null)
        {
            _queue = queue ?? throw new ArgumentNullException(nameof(queue));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _serializer = serializer ?? new JsonJobSerializer();
        }

        /// <summary>
        /// Enqueues a parameterless job for immediate processing.
        /// </summary>
        public Task<Guid> EnqueueAsync<TJob>(CancellationToken cancellationToken = default) where TJob : IJob
        {
            var descriptor = new JobDescriptor
            {
                JobType = typeof(TJob).AssemblyQualifiedName!
            };
            return _queue.EnqueueAsync(descriptor, cancellationToken);
        }

        /// <summary>
        /// Enqueues a job with input data for immediate processing.
        /// </summary>
        public Task<Guid> EnqueueAsync<TJob, TInput>(TInput input, CancellationToken cancellationToken = default)
            where TJob : IJob<TInput>
            where TInput : class
        {
            var descriptor = new JobDescriptor
            {
                JobType = typeof(TJob).AssemblyQualifiedName!,
                InputType = typeof(TInput).AssemblyQualifiedName!,
                SerializedInput = _serializer.Serialize(input)
            };
            return _queue.EnqueueAsync(descriptor, cancellationToken);
        }

        /// <summary>
        /// Enqueues a job to run after a delay.
        /// </summary>
        public Task<Guid> ScheduleAsync<TJob>(TimeSpan delay, CancellationToken cancellationToken = default) where TJob : IJob
        {
            var now = _clock.UtcNow;
            var descriptor = new JobDescriptor
            {
                JobType = typeof(TJob).AssemblyQualifiedName!,
                Delay = delay,
                ScheduledAt = now.Add(delay),
                Status = JobStatus.Scheduled
            };
            return _queue.EnqueueAsync(descriptor, cancellationToken);
        }

        /// <summary>
        /// Enqueues a job with input data to run after a delay.
        /// </summary>
        public Task<Guid> ScheduleAsync<TJob, TInput>(TInput input, TimeSpan delay, CancellationToken cancellationToken = default)
            where TJob : IJob<TInput>
            where TInput : class
        {
            var now = _clock.UtcNow;
            var descriptor = new JobDescriptor
            {
                JobType = typeof(TJob).AssemblyQualifiedName!,
                InputType = typeof(TInput).AssemblyQualifiedName!,
                SerializedInput = _serializer.Serialize(input),
                Delay = delay,
                ScheduledAt = now.Add(delay),
                Status = JobStatus.Scheduled
            };
            return _queue.EnqueueAsync(descriptor, cancellationToken);
        }

        /// <summary>
        /// Enqueues a job on a specific queue.
        /// </summary>
        public Task<Guid> EnqueueOnAsync<TJob>(string queueName, CancellationToken cancellationToken = default) where TJob : IJob
        {
            var descriptor = new JobDescriptor
            {
                JobType = typeof(TJob).AssemblyQualifiedName!,
                QueueName = queueName
            };
            return _queue.EnqueueAsync(descriptor, cancellationToken);
        }

        /// <summary>
        /// Enqueues a job with priority.
        /// </summary>
        public Task<Guid> EnqueueWithPriorityAsync<TJob>(int priority, CancellationToken cancellationToken = default) where TJob : IJob
        {
            var descriptor = new JobDescriptor
            {
                JobType = typeof(TJob).AssemblyQualifiedName!,
                Priority = priority
            };
            return _queue.EnqueueAsync(descriptor, cancellationToken);
        }

        /// <summary>
        /// Cancels a previously enqueued job.
        /// </summary>
        public Task<bool> CancelAsync(Guid jobId, CancellationToken cancellationToken = default)
        {
            return _queue.CancelAsync(jobId, cancellationToken);
        }

        /// <summary>
        /// Gets the status of a job.
        /// </summary>
        public async Task<JobStatus?> GetStatusAsync(Guid jobId, CancellationToken cancellationToken = default)
        {
            var descriptor = await _queue.GetAsync(jobId, cancellationToken).ConfigureAwait(false);
            return descriptor?.Status;
        }
    }
}
