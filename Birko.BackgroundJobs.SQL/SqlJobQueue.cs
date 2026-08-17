using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Birko.BackgroundJobs.SQL.Models;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.Stores;
using Birko.Data.Stores;

namespace Birko.BackgroundJobs.SQL
{
    /// <summary>
    /// SQL-based persistent job queue using Birko.Data.SQL stores.
    /// Works with any SQL connector (PostgreSQL, MSSql, MySQL, SQLite).
    /// Jobs survive process restarts and support distributed processing.
    /// </summary>
    /// <typeparam name="DB">The SQL connector type (e.g., PostgreSqlConnector, MSSqlConnector).</typeparam>
    public class SqlJobQueue<DB> : IJobQueue
        where DB : AbstractConnector
    {
        private readonly AsyncDataBaseBulkStore<DB, JobDescriptorModel> _store;
        private readonly RetryPolicy _retryPolicy;

        /// <summary>
        /// How long a job may sit in <see cref="JobStatus.Processing"/> before it is presumed abandoned
        /// and offered to another worker.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Defaults to <c>JobQueueOptions.JobTimeout</c>'s own default of 30 minutes, and that is a
        /// DERIVED bound rather than a chosen one: the processor cancels a job at <c>JobTimeout</c>, so a
        /// row still <c>Processing</c> after longer than that cannot be legitimately running — whoever
        /// held it is gone. A host that raises <c>JobTimeout</c> must raise this to match, or it will
        /// reclaim jobs that are still working.
        /// </para>
        /// <para>
        /// ⚠ It is a presumption, not knowledge. Nothing here can tell a dead holder from a slow one, so
        /// a job that outlives the timeout while genuinely running WILL be handed to a second worker.
        /// That is why reclaiming is only safe for idempotent jobs, and why the alternative — leaving the
        /// row stranded forever — was the previous behaviour rather than an oversight.
        /// </para>
        /// </remarks>
        private readonly TimeSpan _claimTimeout;

        /// <summary>
        /// Creates a new SQL job queue.
        /// </summary>
        /// <param name="settings">Connection settings for the SQL database.</param>
        /// <param name="retryPolicy">Default retry policy for failed jobs.</param>
        public SqlJobQueue(SqlSettings settings, RetryPolicy? retryPolicy = null, TimeSpan? claimTimeout = null)
        {
            _store = new AsyncDataBaseBulkStore<DB, JobDescriptorModel>();
            _store.SetSettings(settings);
            _retryPolicy = retryPolicy ?? RetryPolicy.Default;
            _claimTimeout = claimTimeout ?? TimeSpan.FromMinutes(30);
        }

        /// <summary>
        /// Creates a new SQL job queue from an existing store.
        /// </summary>
        /// <param name="store">A pre-configured store instance.</param>
        /// <param name="retryPolicy">Default retry policy for failed jobs.</param>
        public SqlJobQueue(AsyncDataBaseBulkStore<DB, JobDescriptorModel> store, RetryPolicy? retryPolicy = null, TimeSpan? claimTimeout = null)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _retryPolicy = retryPolicy ?? RetryPolicy.Default;
            _claimTimeout = claimTimeout ?? TimeSpan.FromMinutes(30);
        }

        /// <summary>
        /// Gets the underlying store for advanced scenarios (e.g., transaction context).
        /// </summary>
        public AsyncDataBaseBulkStore<DB, JobDescriptorModel> Store => _store;

        public async Task<Guid> EnqueueAsync(JobDescriptor descriptor, CancellationToken cancellationToken = default)
        {
            var model = JobDescriptorModel.FromDescriptor(descriptor);
            var id = await _store.CreateAsync(model, ct: cancellationToken).ConfigureAwait(false);
            return id;
        }

        /// <summary>
        /// Maximum number of candidate rows to skip when losing an atomic-claim race to a
        /// concurrent worker before giving up for this poll.
        /// </summary>
        private const int MaxClaimAttempts = 32;

        public async Task<JobDescriptor?> DequeueAsync(string? queueName = null, CancellationToken cancellationToken = default)
        {
            var pendingStatus = (int)JobStatus.Pending;
            var scheduledStatus = (int)JobStatus.Scheduled;
            var processingStatus = (int)JobStatus.Processing;

            for (int attempt = 0; attempt < MaxClaimAttempts; attempt++)
            {
                var now = DateTime.UtcNow;

                // TASK-451: a row left Processing by a worker that died is offered again once it has been
                // held longer than the claim timeout. Before this, DequeueAsync selected only Pending and
                // due-Scheduled rows, so an interrupted job was stranded FOREVER — durability created that
                // state, because the in-memory queue simply lost the whole queue on restart instead.
                var staleBefore = now - _claimTimeout;

                // Find the next eligible job.
                IEnumerable<JobDescriptorModel> candidates;
                if (queueName != null)
                {
                    candidates = await _store.ReadAsync(
                        filter: j => (j.Status == pendingStatus
                                   || (j.Status == scheduledStatus && j.ScheduledAt != null && j.ScheduledAt <= now)
                                   || (j.Status == processingStatus && j.LastAttemptAt != null && j.LastAttemptAt < staleBefore))
                                  && (j.QueueName == null || j.QueueName == queueName),
                        orderBy: OrderBy<JobDescriptorModel>.ByDescending(j => j.Priority).ThenBy(j => j.EnqueuedAt),
                        limit: 1,
                        ct: cancellationToken
                    ).ConfigureAwait(false);
                }
                else
                {
                    candidates = await _store.ReadAsync(
                        filter: j => j.Status == pendingStatus
                                  || (j.Status == scheduledStatus && j.ScheduledAt != null && j.ScheduledAt <= now)
                                  || (j.Status == processingStatus && j.LastAttemptAt != null && j.LastAttemptAt < staleBefore),
                        orderBy: OrderBy<JobDescriptorModel>.ByDescending(j => j.Priority).ThenBy(j => j.EnqueuedAt),
                        limit: 1,
                        ct: cancellationToken
                    ).ConfigureAwait(false);
                }

                var candidate = candidates.FirstOrDefault();
                if (candidate == null)
                {
                    return null;
                }

                // Atomically claim the row: a single native UPDATE ... SET Status=Processing,
                // ClaimToken=<token> WHERE Guid=<id> AND Status=<eligible status>. Only one
                // concurrent worker's WHERE can match (the DB serializes the row write), so the
                // read-then-write gap that let two workers claim the same job is closed (CR-H011).
                var claimId = candidate.Guid;
                var originalStatus = candidate.Status;
                var claimToken = Guid.NewGuid();

                // TASK-451: reclaiming has to be bounded, or a job that kills its worker every time is
                // handed round forever and the failure never surfaces. MaxRetries is already on the row
                // and already means "how many attempts this job gets", so it is reused rather than a
                // second cap invented beside it.
                if (originalStatus == processingStatus && candidate.AttemptCount >= candidate.MaxRetries)
                {
                    candidate.Status = (int)JobStatus.Dead;
                    candidate.CompletedAt = now;
                    candidate.LastError = $"Abandoned: held in Processing for longer than {_claimTimeout} "
                        + $"after {candidate.AttemptCount} attempt(s), and the attempt budget is spent.";
                    await _store.UpdateAsync(candidate, ct: cancellationToken).ConfigureAwait(false);
                    continue;
                }

                await _store.UpdateAsync(
                    filter: j => j.Guid == claimId && j.Status == originalStatus,
                    updates: new PropertyUpdate<JobDescriptorModel>()
                        .Set(j => j.Status, processingStatus)
                        .Set(j => j.ClaimToken, claimToken)
                        .Set(j => j.AttemptCount, candidate.AttemptCount + 1)
                        .Set(j => j.LastAttemptAt, now),
                    ct: cancellationToken
                ).ConfigureAwait(false);

                // Confirm we won the claim (the API exposes no rows-affected count, so verify via
                // the token). If another worker won, skip this row and try the next candidate.
                var claimed = await _store.ReadAsync(j => j.Guid == claimId, cancellationToken).ConfigureAwait(false);
                if (claimed != null && claimed.ClaimToken == claimToken)
                {
                    return claimed.ToDescriptor();
                }
            }

            return null;
        }

        public async Task CompleteAsync(Guid jobId, CancellationToken cancellationToken = default)
        {
            var model = await _store.ReadAsync(j => j.Guid == jobId, cancellationToken).ConfigureAwait(false);
            if (model == null) return;

            model.Status = (int)JobStatus.Completed;
            model.CompletedAt = DateTime.UtcNow;

            await _store.UpdateAsync(model, ct: cancellationToken).ConfigureAwait(false);
        }

        public async Task FailAsync(Guid jobId, string error, CancellationToken cancellationToken = default)
        {
            var model = await _store.ReadAsync(j => j.Guid == jobId, cancellationToken).ConfigureAwait(false);
            if (model == null) return;

            model.LastError = error;

            if (model.AttemptCount < model.MaxRetries)
            {
                var delay = _retryPolicy.GetDelay(model.AttemptCount);
                model.Status = (int)JobStatus.Scheduled;
                model.ScheduledAt = DateTime.UtcNow.Add(delay);
            }
            else
            {
                model.Status = (int)JobStatus.Dead;
                model.CompletedAt = DateTime.UtcNow;
            }

            await _store.UpdateAsync(model, ct: cancellationToken).ConfigureAwait(false);
        }

        public async Task<bool> CancelAsync(Guid jobId, CancellationToken cancellationToken = default)
        {
            var pendingStatus = (int)JobStatus.Pending;
            var scheduledStatus = (int)JobStatus.Scheduled;

            var model = await _store.ReadAsync(
                j => j.Guid == jobId && (j.Status == pendingStatus || j.Status == scheduledStatus),
                cancellationToken
            ).ConfigureAwait(false);

            if (model == null) return false;

            model.Status = (int)JobStatus.Cancelled;
            model.CompletedAt = DateTime.UtcNow;

            await _store.UpdateAsync(model, ct: cancellationToken).ConfigureAwait(false);
            return true;
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
                orderBy: OrderBy<JobDescriptorModel>.ByDescending(j => j.EnqueuedAt),
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
