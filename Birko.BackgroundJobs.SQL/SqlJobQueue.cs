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
        private bool _initialized;

        /// <summary>
        /// Creates a new SQL job queue.
        /// </summary>
        /// <param name="settings">Connection settings for the SQL database.</param>
        /// <param name="retryPolicy">Default retry policy for failed jobs.</param>
        public SqlJobQueue(PasswordSettings settings, RetryPolicy? retryPolicy = null)
        {
            _store = new AsyncDataBaseBulkStore<DB, JobDescriptorModel>();
            _store.SetSettings(settings);
            _retryPolicy = retryPolicy ?? RetryPolicy.Default;
        }

        /// <summary>
        /// Creates a new SQL job queue from an existing store.
        /// </summary>
        /// <param name="store">A pre-configured store instance.</param>
        /// <param name="retryPolicy">Default retry policy for failed jobs.</param>
        public SqlJobQueue(AsyncDataBaseBulkStore<DB, JobDescriptorModel> store, RetryPolicy? retryPolicy = null)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _retryPolicy = retryPolicy ?? RetryPolicy.Default;
        }

        /// <summary>
        /// Gets the underlying store for advanced scenarios (e.g., transaction context).
        /// </summary>
        public AsyncDataBaseBulkStore<DB, JobDescriptorModel> Store => _store;

        public async Task<Guid> EnqueueAsync(JobDescriptor descriptor, CancellationToken cancellationToken = default)
        {
            await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

            var model = JobDescriptorModel.FromDescriptor(descriptor);
            var id = await _store.CreateAsync(model, ct: cancellationToken).ConfigureAwait(false);
            return id;
        }

        public async Task<JobDescriptor?> DequeueAsync(string? queueName = null, CancellationToken cancellationToken = default)
        {
            await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

            var now = DateTime.UtcNow;
            var pendingStatus = (int)JobStatus.Pending;
            var scheduledStatus = (int)JobStatus.Scheduled;

            // Find the next eligible job
            IEnumerable<JobDescriptorModel> candidates;

            if (queueName != null)
            {
                candidates = await _store.ReadAsync(
                    filter: j => (j.Status == pendingStatus || (j.Status == scheduledStatus && j.ScheduledAt != null && j.ScheduledAt <= now))
                              && (j.QueueName == null || j.QueueName == queueName),
                    orderBy: OrderBy<JobDescriptorModel>.ByDescending(j => j.Priority).ThenBy(j => j.EnqueuedAt),
                    limit: 1,
                    ct: cancellationToken
                ).ConfigureAwait(false);
            }
            else
            {
                candidates = await _store.ReadAsync(
                    filter: j => j.Status == pendingStatus || (j.Status == scheduledStatus && j.ScheduledAt != null && j.ScheduledAt <= now),
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

            // Mark as processing
            var processingStatus = (int)JobStatus.Processing;
            candidate.Status = processingStatus;
            candidate.AttemptCount++;
            candidate.LastAttemptAt = DateTime.UtcNow;

            await _store.UpdateAsync(candidate, ct: cancellationToken).ConfigureAwait(false);

            return candidate.ToDescriptor();
        }

        public async Task CompleteAsync(Guid jobId, CancellationToken cancellationToken = default)
        {
            await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

            var model = await _store.ReadAsync(j => j.Guid == jobId, cancellationToken).ConfigureAwait(false);
            if (model == null) return;

            model.Status = (int)JobStatus.Completed;
            model.CompletedAt = DateTime.UtcNow;

            await _store.UpdateAsync(model, ct: cancellationToken).ConfigureAwait(false);
        }

        public async Task FailAsync(Guid jobId, string error, CancellationToken cancellationToken = default)
        {
            await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

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
            await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

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
            await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

            var model = await _store.ReadAsync(j => j.Guid == jobId, cancellationToken).ConfigureAwait(false);
            return model?.ToDescriptor();
        }

        public async Task<IReadOnlyList<JobDescriptor>> GetByStatusAsync(JobStatus status, int limit = 100, CancellationToken cancellationToken = default)
        {
            await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

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
            await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

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

        private async Task EnsureInitializedAsync(CancellationToken cancellationToken)
        {
            if (_initialized) return;

            await _store.InitAsync(cancellationToken).ConfigureAwait(false);
            _initialized = true;
        }
    }
}
