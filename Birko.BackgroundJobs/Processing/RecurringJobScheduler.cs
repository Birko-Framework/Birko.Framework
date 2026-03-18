using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Birko.Time;

namespace Birko.BackgroundJobs.Processing
{
    /// <summary>
    /// Schedules recurring jobs at fixed intervals using cron-like scheduling.
    /// </summary>
    public class RecurringJobScheduler
    {
        private readonly IJobQueue _queue;
        private readonly IDateTimeProvider _clock;
        private readonly ConcurrentDictionary<string, RecurringJobDefinition> _definitions = new();

        public RecurringJobScheduler(IJobQueue queue, IDateTimeProvider clock)
        {
            _queue = queue ?? throw new ArgumentNullException(nameof(queue));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        }

        /// <summary>
        /// Registers a recurring job that fires at a fixed interval.
        /// </summary>
        /// <typeparam name="TJob">The job type to execute.</typeparam>
        /// <param name="name">Unique name for this recurring job.</param>
        /// <param name="interval">How often the job should run.</param>
        /// <param name="queueName">Optional queue name.</param>
        public void Register<TJob>(string name, TimeSpan interval, string? queueName = null) where TJob : IJob
        {
            var definition = new RecurringJobDefinition
            {
                Name = name,
                JobType = typeof(TJob).AssemblyQualifiedName!,
                Interval = interval,
                QueueName = queueName,
                NextRunAt = _clock.UtcNow.Add(interval)
            };

            _definitions.AddOrUpdate(name, definition, (_, _) => definition);
        }

        /// <summary>
        /// Removes a recurring job by name.
        /// </summary>
        public bool Remove(string name)
        {
            return _definitions.TryRemove(name, out _);
        }

        /// <summary>
        /// Runs the scheduler loop, enqueuing jobs when their interval elapses.
        /// </summary>
        public async Task RunAsync(CancellationToken cancellationToken = default)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var now = _clock.UtcNow;

                foreach (var kvp in _definitions)
                {
                    var def = kvp.Value;
                    if (now >= def.NextRunAt)
                    {
                        var descriptor = new JobDescriptor
                        {
                            JobType = def.JobType,
                            QueueName = def.QueueName,
                            Metadata = { ["recurring.name"] = def.Name }
                        };

                        await _queue.EnqueueAsync(descriptor, cancellationToken).ConfigureAwait(false);
                        def.NextRunAt = now.Add(def.Interval);
                        def.LastRunAt = now;
                    }
                }

                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }

    /// <summary>
    /// Internal definition of a recurring job schedule.
    /// </summary>
    internal class RecurringJobDefinition
    {
        public string Name { get; set; } = string.Empty;
        public string JobType { get; set; } = string.Empty;
        public TimeSpan Interval { get; set; }
        public string? QueueName { get; set; }
        public DateTime NextRunAt { get; set; }
        public DateTime? LastRunAt { get; set; }
    }
}
