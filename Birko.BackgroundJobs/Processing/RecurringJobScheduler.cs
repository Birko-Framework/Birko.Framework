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
    /// <remarks>
    /// <para>
    /// <b>The schedule lives in this process's memory.</b> <c>NextRunAt</c> is a field on an in-memory
    /// definition, so N workers running this scheduler each conclude independently that a job is due and
    /// each enqueue their own copy — N copies of every recurring job, on every backend. A durable queue does
    /// not help: its atomic dequeue makes <i>claiming</i> a job safe, not <i>deciding to enqueue</i> one.
    /// </para>
    /// <para>
    /// <b>Leader election is the fix, and it is opt-in.</b> Pass an <see cref="IJobLockProvider"/> and only
    /// the process holding the named lock runs the schedule. Pass nothing — the default — and behaviour is
    /// exactly what it was, because six of the eight job backends cannot express a lock at all (TASK-236).
    /// </para>
    /// <para>
    /// The rejected alternative is worth recording, because it looks like the obvious one: locking each
    /// individual decision (a lock named for the due instant, only the winner enqueues) <b>does not work
    /// with a lock</b>. Every process releases immediately after enqueueing, so one whose loop or clock runs
    /// a few seconds behind arrives later, finds the lock free and enqueues a duplicate. Closing that means
    /// holding the lock until the <i>next</i> due instant, at which point it is not a lock but a persistent
    /// record saying "10:00 was already enqueued". <b>"Has this occurrence already been enqueued?" is an
    /// idempotency question, not a mutual-exclusion one</b>, and its right answer is a unique key on the
    /// queue — which would work on all eight backends rather than the two that can express a lock. Recorded
    /// as the better long-term shape; not what this class does (TASK-237).
    /// </para>
    /// <para>
    /// ⚠ The provider instance must be <b>dedicated to this scheduler</b>. Both shipped implementations hold
    /// one lock per provider and expose a single <see cref="IJobLockProvider.IsLocked"/> flag, so sharing an
    /// instance with another caller makes that flag answer about the wrong lock.
    /// </para>
    /// </remarks>
    public class RecurringJobScheduler
    {
        /// <summary>
        /// Lock name used when the caller does not supply one. Deliberately unprefixed: a provider adds its
        /// own namespace (<c>RedisJobLockProvider</c> prepends its key prefix).
        /// </summary>
        public const string DefaultLockName = "recurring-scheduler";

        /// <summary>
        /// How often a follower re-attempts acquisition when the caller does not say.
        /// </summary>
        /// <remarks>
        /// Not every tick: <c>SqlJobLockProvider</c> opens and closes a real connection per attempt, so a
        /// one-second poll would cost a connection per second per follower for a handover that is allowed to
        /// take a few seconds.
        /// </remarks>
        public static readonly TimeSpan DefaultLeadershipRetryInterval = TimeSpan.FromSeconds(15);

        private readonly IJobQueue _queue;
        private readonly IDateTimeProvider _clock;
        private readonly ConcurrentDictionary<string, RecurringJobDefinition> _definitions = new();
        private readonly IJobLockProvider? _lockProvider;
        private readonly string _lockName;
        private readonly TimeSpan _leadershipRetryInterval;
        private bool _isLeader;
        private DateTime _nextLeadershipAttemptAt = DateTime.MinValue;

        /// <summary>
        /// Creates a scheduler, optionally coordinated across processes by leader election.
        /// </summary>
        /// <param name="queue">Queue that receives the enqueued occurrences.</param>
        /// <param name="clock">Clock the schedule is measured against.</param>
        /// <param name="lockProvider">
        /// Optional. When supplied, only the process holding <paramref name="lockName"/> enqueues.
        /// <c>null</c> — the default — means every instance schedules, which is the behaviour every existing
        /// consumer already has.
        /// </param>
        /// <param name="lockName">Name of the leadership lock. All schedulers meant to elect one leader
        /// between them must share it.</param>
        /// <param name="leadershipRetryInterval">
        /// How often a follower re-attempts acquisition. Defaults to
        /// <see cref="DefaultLeadershipRetryInterval"/>.
        /// </param>
        public RecurringJobScheduler(
            IJobQueue queue,
            IDateTimeProvider clock,
            IJobLockProvider? lockProvider = null,
            string lockName = DefaultLockName,
            TimeSpan? leadershipRetryInterval = null)
        {
            _queue = queue ?? throw new ArgumentNullException(nameof(queue));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _lockProvider = lockProvider;

            if (string.IsNullOrWhiteSpace(lockName))
            {
                throw new ArgumentException("A leadership lock needs a name.", nameof(lockName));
            }
            _lockName = lockName;

            var retry = leadershipRetryInterval ?? DefaultLeadershipRetryInterval;
            if (retry <= TimeSpan.Zero)
            {
                throw new ArgumentException(
                    "A leadership retry interval must be positive.", nameof(leadershipRetryInterval));
            }
            _leadershipRetryInterval = retry;
        }

        /// <summary>
        /// Whether this scheduler is currently entitled to enqueue.
        /// </summary>
        /// <remarks>
        /// Always <c>true</c> without a lock provider — an uncoordinated scheduler always leads, which is
        /// what makes the provider additive. With one, this is the state as of the last loop tick, so it is
        /// this scheduler's belief rather than a guarantee about the world; see
        /// <see cref="IJobLockProvider"/>.
        /// </remarks>
        public bool IsLeader => _lockProvider == null || (_isLeader && _lockProvider.IsLocked);

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
        /// <remarks>
        /// With a lock provider the loop enqueues only while it leads, and keeps knocking while it does not,
        /// so the death of a leader is recovered from without restarting every worker.
        /// </remarks>
        public async Task RunAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    bool leads;
                    try
                    {
                        leads = await EnsureLeadershipAsync(cancellationToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        // Cancelling this loop has always completed it rather than faulted it — the
                        // pre-existing catch around Task.Delay says so. A provider must not change that
                        // for the consumers who opt into one.
                        break;
                    }

                    if (leads)
                    {
                        var now = _clock.UtcNow;

                        foreach (var kvp in _definitions)
                        {
                            // Re-checked per definition, not just per tick: a lease can expire under us
                            // mid-pass, and the promise is that nothing is enqueued once the loss is
                            // known — not that nothing is enqueued in a tick that began with the loss.
                            if (!IsLeader)
                            {
                                break;
                            }

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
            finally
            {
                await ReleaseLeadershipAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Answers whether this tick may enqueue, acquiring or re-acquiring leadership as needed.
        /// </summary>
        private async Task<bool> EnsureLeadershipAsync(CancellationToken cancellationToken)
        {
            // No provider is not "assume leadership": it is the uncoordinated behaviour every existing
            // consumer has today, kept bit-for-bit so this whole feature is additive.
            if (_lockProvider == null)
            {
                return true;
            }

            // Never cache leadership across ticks. On an IsLeaseBased provider IsLocked goes false on its
            // own — Redis clears it when a renewal finds the key gone — and a scheduler that carried on
            // believing it led would be the second leader the lease was supposed to prevent.
            if (_isLeader)
            {
                if (_lockProvider.IsLocked)
                {
                    return true;
                }

                _isLeader = false;
            }

            var now = _clock.UtcNow;
            if (now < _nextLeadershipAttemptAt)
            {
                return false;
            }
            _nextLeadershipAttemptAt = now.Add(_leadershipRetryInterval);

            bool acquired;
            try
            {
                // TimeSpan.Zero: a follower must not block its own loop waiting for a leader that may hold
                // the lock for days. null lease asks for a session-scoped lock, which is the contract's
                // default — a lease-based provider reads it as "use my own", and a session-based one
                // throws on anything else.
                acquired = await _lockProvider
                    .TryAcquireAsync(_lockName, TimeSpan.Zero, null, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Ours: let RunAsync end the loop. A cancellation from anywhere else is just a knock that
                // did not land, and is handled below — treating it as shutdown would stop scheduling for
                // good over someone else's timeout.
                throw;
            }
            catch
            {
                // A backend that is down must not tear the loop down with it. A follower that stopped
                // re-attempting on one failed knock would leave nothing scheduling until it restarted,
                // which is the failure leader election exists to avoid.
                return false;
            }

            if (!acquired)
            {
                return false;
            }

            RebaseSchedule(now);
            _isLeader = true;
            return true;
        }

        /// <summary>
        /// Restarts every definition's schedule from <paramref name="now"/> on becoming leader.
        /// </summary>
        /// <remarks>
        /// A new leader has no idea what the previous one enqueued, and its own <c>NextRunAt</c> values
        /// stood still while it was a follower — so without this it fires every elapsed occurrence the
        /// moment it takes over, duplicating exactly the work the old leader already did. That is why a
        /// follower leaves <c>NextRunAt</c> alone rather than advancing it: advancing is a scheduling
        /// decision a follower is not entitled to make, and code that advances is one edit away from code
        /// that enqueues.
        /// </remarks>
        private void RebaseSchedule(DateTime now)
        {
            foreach (var kvp in _definitions)
            {
                kvp.Value.NextRunAt = now.Add(kvp.Value.Interval);
            }
        }

        private async Task ReleaseLeadershipAsync()
        {
            if (_lockProvider == null || !_isLeader)
            {
                return;
            }

            _isLeader = false;
            try
            {
                // CancellationToken.None deliberately: the loop is exiting *because* its token was
                // cancelled, and both providers' ReleaseAsync open with ThrowIfCancellationRequested — so
                // passing it through would skip the release on the one path that always takes it, leaving a
                // session lock held until the connection drops.
                await _lockProvider.ReleaseAsync(_lockName, CancellationToken.None).ConfigureAwait(false);
            }
            catch
            {
                // Best effort. A session provider frees the lock when this process goes away, and a lease
                // expires on its own once the heartbeat stops; neither is worth faulting a shutdown over.
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
