using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Birko.BackgroundJobs.ElasticSearch.Models;
using Birko.Data.ElasticSearch.Stores;
using Birko.Data.Stores;
using Birko.Configuration;

namespace Birko.BackgroundJobs.ElasticSearch
{
    /// <summary>
    /// Elasticsearch-based persistent job queue using Birko.Data.ElasticSearch stores.
    /// Jobs are stored as documents in an Elasticsearch index.
    /// </summary>
    public class ElasticSearchJobQueue : IJobQueue
    {
        private readonly AsyncElasticSearchStore<ElasticJobDescriptorModel> _store;
        private readonly RetryPolicy _retryPolicy;

        /// <summary>How long a job may stay Processing before it is presumed abandoned (TASK-664; see <see cref="JobReclaim"/>).</summary>
        private readonly TimeSpan _claimTimeout;

        /// <summary>
        /// Creates a new Elasticsearch job queue.
        /// </summary>
        public ElasticSearchJobQueue(Birko.Data.ElasticSearch.Stores.Settings settings, RetryPolicy? retryPolicy = null, TimeSpan? claimTimeout = null)
        {
            _store = new AsyncElasticSearchStore<ElasticJobDescriptorModel>();
            _store.SetSettings(settings);
            _retryPolicy = retryPolicy ?? RetryPolicy.Default;
            _claimTimeout = JobReclaim.Resolve(claimTimeout);
        }

        /// <summary>
        /// Creates a new Elasticsearch job queue from an existing store.
        /// </summary>
        public ElasticSearchJobQueue(AsyncElasticSearchStore<ElasticJobDescriptorModel> store, RetryPolicy? retryPolicy = null, TimeSpan? claimTimeout = null)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _retryPolicy = retryPolicy ?? RetryPolicy.Default;
            _claimTimeout = JobReclaim.Resolve(claimTimeout);
        }

        /// <summary>
        /// Gets the underlying store for advanced scenarios.
        /// </summary>
        public AsyncElasticSearchStore<ElasticJobDescriptorModel> Store => _store;

        public async Task<Guid> EnqueueAsync(JobDescriptor descriptor, CancellationToken cancellationToken = default)
        {
            var model = ElasticJobDescriptorModel.FromDescriptor(descriptor);
            var id = await _store.CreateAsync(model, ct: cancellationToken).ConfigureAwait(false);
            return id;
        }

        public async Task<JobDescriptor?> DequeueAsync(string? queueName = null, CancellationToken cancellationToken = default)
        {
            var now = DateTime.UtcNow;
            // TASK-664: a job left Processing longer than the claim timeout is a candidate too, as on SQL. The
            // compare-and-set re-checks staleness on the stored document, and the winner stamps LastAttemptAt.
            var staleBefore = now - _claimTimeout;

            // TASK-667: each candidate is claimed by a compare-and-set on the document (a realtime GET, written back
            // conditioned on its _seq_no / _primary_term), with eligibility re-checked on what is stored. The search that
            // finds candidates lags the index by up to a refresh, so a candidate is only a hint. A lost race moves on.
            for (var round = 0; round < MaxClaimRounds; round++)
            {
                var candidates = await ReadCandidatesAsync(now, staleBefore, queueName, cancellationToken).ConfigureAwait(false);
                if (candidates.Count == 0) return null;

                foreach (var candidate in candidates)
                {
                    if (candidate.Status == (int)JobStatus.Processing && candidate.AttemptCount >= _retryPolicy.MaxRetriesFor(candidate.MaxRetries))
                    {
                        // Abandoned with no attempts left: buried, under the same staleness condition, never claimed.
                        await _store.UpdateIfAsync(candidate.Guid!.Value, m => IsStale(m, staleBefore), m =>
                        {
                            m.Status = (int)JobStatus.Dead;
                            m.CompletedAt = now;
                            m.LastError = JobReclaim.AbandonedError(_claimTimeout, m.AttemptCount);
                        }, cancellationToken).ConfigureAwait(false);
                        continue;
                    }

                    var claimed = await _store.UpdateIfAsync(
                        candidate.Guid!.Value,
                        m => IsDue(m, now, staleBefore, queueName),
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

        private static bool IsDue(ElasticJobDescriptorModel m, DateTime now, DateTime staleBefore, string? queueName)
            => (m.Status == (int)JobStatus.Pending || (m.Status == (int)JobStatus.Scheduled && m.ScheduledAt != null && m.ScheduledAt <= now)
                    || IsStale(m, staleBefore))
                && (queueName == null || m.QueueName == null || m.QueueName == queueName);

        private static bool IsStale(ElasticJobDescriptorModel m, DateTime staleBefore)
            => m.Status == (int)JobStatus.Processing && m.LastAttemptAt != null && m.LastAttemptAt < staleBefore;

        private async Task<List<ElasticJobDescriptorModel>> ReadCandidatesAsync(DateTime now, DateTime staleBefore, string? queueName, CancellationToken cancellationToken)
        {
            var pendingStatus = (int)JobStatus.Pending;
            var scheduledStatus = (int)JobStatus.Scheduled;
            var processingStatus = (int)JobStatus.Processing;
            // The search lags by up to a refresh: a candidate it returns is only a hint, re-checked on the document by the claim.
            IEnumerable<ElasticJobDescriptorModel> candidates = queueName != null
                ? await _store.ReadAsync(
                    filter: j => (j.Status == pendingStatus || (j.Status == scheduledStatus && j.ScheduledAt != null && j.ScheduledAt <= now)
                                  || (j.Status == processingStatus && j.LastAttemptAt != null && j.LastAttemptAt < staleBefore))
                              && (j.QueueName == null || j.QueueName == queueName),
                    orderBy: OrderBy<ElasticJobDescriptorModel>.ByDescending(j => j.Priority).ThenBy(j => j.EnqueuedAt),
                    limit: ClaimBatch,
                    ct: cancellationToken
                ).ConfigureAwait(false)
                : await _store.ReadAsync(
                    filter: j => j.Status == pendingStatus || (j.Status == scheduledStatus && j.ScheduledAt != null && j.ScheduledAt <= now)
                              || (j.Status == processingStatus && j.LastAttemptAt != null && j.LastAttemptAt < staleBefore),
                    orderBy: OrderBy<ElasticJobDescriptorModel>.ByDescending(j => j.Priority).ThenBy(j => j.EnqueuedAt),
                    limit: ClaimBatch,
                    ct: cancellationToken
                ).ConfigureAwait(false);
            return candidates.ToList();
        }

        // TASK-667: each transition is a compare-and-set on the document, with the condition checked on what is stored.
        // TASK-158 made them update_by_query calls, which match the search index: a job claimed under a second ago was
        // still indexed Pending, so Complete and Fail matched nothing and the outcome was dropped (measured live), and
        // Cancel cancelled a running job.
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
            // TASK-667: realtime, not a search — a job read back right after a transition was its pre-transition copy.
            var model = await _store.ReadRealtimeAsync(jobId, cancellationToken).ConfigureAwait(false);
            return model?.ToDescriptor();
        }


        public async Task<IReadOnlyList<JobDescriptor>> GetByStatusAsync(JobStatus status, int limit = 100, CancellationToken cancellationToken = default)
        {
            var statusInt = (int)status;

            var models = await _store.ReadAsync(
                filter: j => j.Status == statusInt,
                orderBy: OrderBy<ElasticJobDescriptorModel>.ByDescending(j => j.EnqueuedAt),
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
