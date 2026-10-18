using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Birko.BackgroundJobs.RavenDB.Models;
using Birko.Data.RavenDB.Stores;
using Birko.Data.Stores;
using Birko.Configuration;

namespace Birko.BackgroundJobs.RavenDB
{
    /// <summary>
    /// RavenDB-based persistent job queue using Birko.Data.RavenDB stores.
    /// Jobs are stored as documents in a RavenDB database.
    /// </summary>
    public class RavenDBJobQueue : IJobQueue
    {
        private readonly AsyncRavenDBStore<RavenJobDescriptorModel> _store;
        private readonly RetryPolicy _retryPolicy;

        /// <summary>
        /// Creates a new RavenDB job queue.
        /// </summary>
        public RavenDBJobQueue(Birko.Data.RavenDB.Stores.Settings settings, RetryPolicy? retryPolicy = null)
        {
            _store = new AsyncRavenDBStore<RavenJobDescriptorModel>();
            _store.SetSettings(settings);
            _retryPolicy = retryPolicy ?? RetryPolicy.Default;
        }

        /// <summary>
        /// Creates a new RavenDB job queue from an existing store.
        /// </summary>
        public RavenDBJobQueue(AsyncRavenDBStore<RavenJobDescriptorModel> store, RetryPolicy? retryPolicy = null)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _retryPolicy = retryPolicy ?? RetryPolicy.Default;
        }

        /// <summary>
        /// Gets the underlying store for advanced scenarios (e.g., transaction context).
        /// </summary>
        public AsyncRavenDBStore<RavenJobDescriptorModel> Store => _store;

        public async Task<Guid> EnqueueAsync(JobDescriptor descriptor, CancellationToken cancellationToken = default)
        {
            var model = RavenJobDescriptorModel.FromDescriptor(descriptor);
            var id = await _store.CreateAsync(model, ct: cancellationToken).ConfigureAwait(false);
            return id;
        }

        public async Task<JobDescriptor?> DequeueAsync(string? queueName = null, CancellationToken cancellationToken = default)
        {
            var now = DateTime.UtcNow;

            // TASK-665: each candidate is claimed by a compare-and-set on the document (loaded by id, saved with its
            // change vector), with eligibility re-checked on what is stored. The candidate query is served by an index
            // that may be stale, and the claim was a filtered update the base store runs as that same query followed by
            // an unconditional write, so a job claimed a moment ago was claimed again (measured: a cancel racing a claim
            // let both win, 3 of 3 runs). A lost race moves on to the next candidate.
            for (var round = 0; round < MaxClaimRounds; round++)
            {
                var candidates = await ReadCandidatesAsync(now, queueName, cancellationToken).ConfigureAwait(false);
                if (candidates.Count == 0) return null;

                foreach (var candidate in candidates)
                {
                    var claimed = await _store.UpdateIfAsync(
                        candidate.Guid!.Value,
                        m => IsDue(m, now, queueName),
                        m =>
                        {
                            m.Status = (int)JobStatus.Processing;
                            m.ClaimToken = Guid.NewGuid();
                            m.AttemptCount++;
                            m.LastAttemptAt = now;
                        },
                        cancellationToken).ConfigureAwait(false);
                    if (claimed != null) return claimed.ToDescriptor();
                }

                if (candidates.Count < ClaimBatch) return null;
            }

            return null;
        }

        /// <summary>Candidates read per round; every one a round loses to another worker is skipped, not retried.</summary>
        private const int ClaimBatch = 8;

        /// <summary>Rounds of candidates one dequeue tries before it reports nothing to do.</summary>
        private const int MaxClaimRounds = 4;

        private static bool IsDue(RavenJobDescriptorModel m, DateTime now, string? queueName)
            => (m.Status == (int)JobStatus.Pending || (m.Status == (int)JobStatus.Scheduled && m.ScheduledAt != null && m.ScheduledAt <= now))
                && (queueName == null || m.QueueName == null || m.QueueName == queueName);

        private async Task<List<RavenJobDescriptorModel>> ReadCandidatesAsync(DateTime now, string? queueName, CancellationToken cancellationToken)
        {
            var pendingStatus = (int)JobStatus.Pending;
            var scheduledStatus = (int)JobStatus.Scheduled;
            // The index may lag: a candidate it returns is only a hint, re-checked on the document by the claim. Two
            // queries, because the RavenDB LINQ provider cannot translate a `queueName == null ||` term on a captured value.
            IEnumerable<RavenJobDescriptorModel> candidates = queueName != null
                ? await _store.ReadAsync(
                    filter: j => (j.Status == pendingStatus || (j.Status == scheduledStatus && j.ScheduledAt != null && j.ScheduledAt <= now))
                              && (j.QueueName == null || j.QueueName == queueName),
                    orderBy: OrderBy<RavenJobDescriptorModel>.ByDescending(j => j.Priority).ThenBy(j => j.EnqueuedAt),
                    limit: ClaimBatch,
                    ct: cancellationToken
                ).ConfigureAwait(false)
                : await _store.ReadAsync(
                    filter: j => j.Status == pendingStatus || (j.Status == scheduledStatus && j.ScheduledAt != null && j.ScheduledAt <= now),
                    orderBy: OrderBy<RavenJobDescriptorModel>.ByDescending(j => j.Priority).ThenBy(j => j.EnqueuedAt),
                    limit: ClaimBatch,
                    ct: cancellationToken
                ).ConfigureAwait(false);
            return candidates.ToList();
        }

        // TASK-665: each transition is a compare-and-set on the document, with the condition checked on what is stored.
        // TASK-158 made them filtered updates, which RavenDB runs as a (possibly stale) query and an unconditional write.
        public async Task CompleteAsync(Guid jobId, CancellationToken cancellationToken = default)
        {
            await _store.UpdateIfAsync(jobId, m => m.Status == (int)JobStatus.Processing, m =>
            {
                m.Status = (int)JobStatus.Completed;
                m.CompletedAt = DateTime.UtcNow;
            }, cancellationToken).ConfigureAwait(false);
        }

        public async Task FailAsync(Guid jobId, string error, CancellationToken cancellationToken = default)
        {
            await _store.UpdateIfAsync(jobId, m => m.Status == (int)JobStatus.Processing, m =>
            {
                m.LastError = error;
                var now = DateTime.UtcNow;
                if (m.AttemptCount < _retryPolicy.MaxRetriesFor(m.MaxRetries))
                {
                    m.ScheduledAt = now.Add(_retryPolicy.GetDelay(m.AttemptCount));
                    m.Status = (int)JobStatus.Scheduled;
                }
                else
                {
                    m.CompletedAt = now;
                    m.Status = (int)JobStatus.Dead;
                }
            }, cancellationToken).ConfigureAwait(false);
        }

        public async Task<bool> CancelAsync(Guid jobId, CancellationToken cancellationToken = default)
        {
            var cancelled = await _store.UpdateIfAsync(jobId,
                m => m.Status == (int)JobStatus.Pending || m.Status == (int)JobStatus.Scheduled,
                m =>
                {
                    m.Status = (int)JobStatus.Cancelled;
                    m.CompletedAt = DateTime.UtcNow;
                }, cancellationToken).ConfigureAwait(false);
            return cancelled != null;
        }

        public async Task<JobDescriptor?> GetAsync(Guid jobId, CancellationToken cancellationToken = default)
        {
            var model = await _store.ReadAsync(j => j.Guid == jobId, cancellationToken).ConfigureAwait(false);
            return model?.ToDescriptor();
        }

        public async Task<IReadOnlyList<JobDescriptor>> GetByStatusAsync(JobStatus status, int limit = 100, CancellationToken cancellationToken = default)
        {
            var statusInt = (int)status;

            var models = await _store.ReadAsync(
                filter: j => j.Status == statusInt,
                orderBy: OrderBy<RavenJobDescriptorModel>.ByDescending(j => j.EnqueuedAt),
                limit: limit,
                ct: cancellationToken
            ).ConfigureAwait(false);

            return models.Select(m => m.ToDescriptor()).ToList();
        }

        public async Task<int> PurgeAsync(TimeSpan olderThan, CancellationToken cancellationToken = default)
        {
            var cutoff = DateTime.UtcNow.Subtract(olderThan);
            var completedStatus = (int)JobStatus.Completed;
            var deadStatus = (int)JobStatus.Dead;
            var cancelledStatus = (int)JobStatus.Cancelled;

            var toPurge = await _store.ReadAsync(
                filter: j => (j.Status == completedStatus || j.Status == deadStatus || j.Status == cancelledStatus)
                          && j.CompletedAt != null && j.CompletedAt < cutoff,
                ct: cancellationToken
            ).ConfigureAwait(false);

            var list = toPurge.ToList();
            if (list.Count > 0)
            {
                await _store.DeleteAsync(list, cancellationToken).ConfigureAwait(false);
            }

            return list.Count;
        }

    }
}
