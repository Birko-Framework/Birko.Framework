using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Birko.BackgroundJobs.XML.Models;
using Birko.Data.XML.Stores;
using Birko.Data.Stores;
using Birko.Configuration;
using Birko.Time;

namespace Birko.BackgroundJobs.XML
{
    /// <summary>
    /// XML file-based job queue using Birko.Data.XML stores.
    /// Good for development, testing, and single-process deployments where
    /// XML-readable audit trails are preferred over JSON.
    /// </summary>
    public class XmlJobQueue : IJobQueue
    {
        private readonly AsyncXmlStore<XmlJobDescriptorModel> _store;
        private readonly RetryPolicy _retryPolicy;
        private readonly IDateTimeProvider _clock;

        // TASK-158 (SH-M021): one store and one gate per file, shared by every queue on that file in the process. Each
        // queue used to keep its own snapshot of the file and its own dequeue lock, so a second queue on the same file
        // (two hosted services, a queue and a schema helper) overwrote the first one's jobs on save, and both could claim
        // the same job. Every operation runs under the gate: the store's rows are a plain Dictionary. Separate
        // processes on one file remain unsupported — the file has no compare-and-swap.
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, (AsyncXmlStore<XmlJobDescriptorModel> Store, SemaphoreSlim Gate)> SharedFiles =
            new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<AsyncXmlStore<XmlJobDescriptorModel>, SemaphoreSlim> StoreGates = new();
        private readonly SemaphoreSlim _gate;

        /// <summary>
        /// Creates a new XML job queue.
        /// </summary>
        public XmlJobQueue(Birko.Configuration.Settings settings, IDateTimeProvider clock, RetryPolicy? retryPolicy = null)
        {
            ArgumentNullException.ThrowIfNull(settings);
            var file = System.IO.Path.GetFullPath(System.IO.Path.Combine(settings.Location ?? string.Empty, settings.Name ?? string.Empty));
            var shared = SharedFiles.GetOrAdd(file, _ =>
            {
                var store = new AsyncXmlStore<XmlJobDescriptorModel>();
                store.SetSettings(settings);
                return (store, new SemaphoreSlim(1, 1));
            });
            _store = shared.Store;
            _gate = shared.Gate;
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _retryPolicy = retryPolicy ?? RetryPolicy.Default;
        }

        /// <summary>
        /// Creates a new XML job queue from an existing store.
        /// </summary>
        public XmlJobQueue(AsyncXmlStore<XmlJobDescriptorModel> store, IDateTimeProvider clock, RetryPolicy? retryPolicy = null)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _gate = StoreGates.GetValue(store, _ => new SemaphoreSlim(1, 1));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _retryPolicy = retryPolicy ?? RetryPolicy.Default;
        }

        /// <summary>
        /// Gets the underlying store for advanced scenarios.
        /// </summary>
        public AsyncXmlStore<XmlJobDescriptorModel> Store => _store;

        public async Task<Guid> EnqueueAsync(JobDescriptor descriptor, CancellationToken cancellationToken = default)
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                return await EnqueueCoreAsync(descriptor, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _gate.Release();
            }
        }

        private async Task<Guid> EnqueueCoreAsync(JobDescriptor descriptor, CancellationToken cancellationToken)
        {
            var model = XmlJobDescriptorModel.FromDescriptor(descriptor);
            var id = await _store.CreateAsync(model, ct: cancellationToken).ConfigureAwait(false);
            return id;
        }

        public async Task<JobDescriptor?> DequeueAsync(string? queueName = null, CancellationToken cancellationToken = default)
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var now = _clock.UtcNow;
                var pendingStatus = (int)JobStatus.Pending;
                var scheduledStatus = (int)JobStatus.Scheduled;

                IEnumerable<XmlJobDescriptorModel> candidates;

                if (queueName != null)
                {
                    candidates = await _store.ReadAsync(
                        filter: j => (j.Status == pendingStatus || (j.Status == scheduledStatus && j.ScheduledAt != null && j.ScheduledAt <= now))
                                  && (j.QueueName == null || j.QueueName == queueName),
                        orderBy: OrderBy<XmlJobDescriptorModel>.ByDescending(j => j.Priority).ThenBy(j => j.EnqueuedAt),
                        limit: 1,
                        ct: cancellationToken
                    ).ConfigureAwait(false);
                }
                else
                {
                    candidates = await _store.ReadAsync(
                        filter: j => j.Status == pendingStatus || (j.Status == scheduledStatus && j.ScheduledAt != null && j.ScheduledAt <= now),
                        orderBy: OrderBy<XmlJobDescriptorModel>.ByDescending(j => j.Priority).ThenBy(j => j.EnqueuedAt),
                        limit: 1,
                        ct: cancellationToken
                    ).ConfigureAwait(false);
                }

                var candidate = candidates.FirstOrDefault();
                if (candidate == null)
                {
                    return null;
                }

                candidate.Status = (int)JobStatus.Processing;
                candidate.AttemptCount++;
                candidate.LastAttemptAt = _clock.UtcNow;

                await _store.UpdateAsync(candidate, ct: cancellationToken).ConfigureAwait(false);

                return candidate.ToDescriptor();
            }
            finally
            {
                _gate.Release();
            }
        }

        public async Task CompleteAsync(Guid jobId, CancellationToken cancellationToken = default)
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await CompleteCoreAsync(jobId, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _gate.Release();
            }
        }

        private async Task CompleteCoreAsync(Guid jobId, CancellationToken cancellationToken)
        {
            var model = await _store.ReadAsync(j => j.Guid == jobId, cancellationToken).ConfigureAwait(false);
            // TASK-158: only a claimed (Processing) job is completed or failed, as on every store-backed queue.
            if (model == null || model.Status != (int)JobStatus.Processing) return;

            model.Status = (int)JobStatus.Completed;
            model.CompletedAt = _clock.UtcNow;

            await _store.UpdateAsync(model, ct: cancellationToken).ConfigureAwait(false);
        }

        public async Task FailAsync(Guid jobId, string error, CancellationToken cancellationToken = default)
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await FailCoreAsync(jobId, error, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _gate.Release();
            }
        }

        private async Task FailCoreAsync(Guid jobId, string error, CancellationToken cancellationToken)
        {
            var model = await _store.ReadAsync(j => j.Guid == jobId, cancellationToken).ConfigureAwait(false);
            // TASK-158: only a claimed (Processing) job is completed or failed, as on every store-backed queue.
            if (model == null || model.Status != (int)JobStatus.Processing) return;

            model.LastError = error;

            if (model.AttemptCount < _retryPolicy.MaxRetriesFor(model.MaxRetries))
            {
                var delay = _retryPolicy.GetDelay(model.AttemptCount);
                model.Status = (int)JobStatus.Scheduled;
                model.ScheduledAt = _clock.UtcNow.Add(delay);
            }
            else
            {
                model.Status = (int)JobStatus.Dead;
                model.CompletedAt = _clock.UtcNow;
            }

            await _store.UpdateAsync(model, ct: cancellationToken).ConfigureAwait(false);
        }

        public async Task<bool> CancelAsync(Guid jobId, CancellationToken cancellationToken = default)
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                return await CancelCoreAsync(jobId, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _gate.Release();
            }
        }

        private async Task<bool> CancelCoreAsync(Guid jobId, CancellationToken cancellationToken)
        {
            var pendingStatus = (int)JobStatus.Pending;
            var scheduledStatus = (int)JobStatus.Scheduled;

            var model = await _store.ReadAsync(
                j => j.Guid == jobId && (j.Status == pendingStatus || j.Status == scheduledStatus),
                cancellationToken
            ).ConfigureAwait(false);

            if (model == null) return false;

            model.Status = (int)JobStatus.Cancelled;
            model.CompletedAt = _clock.UtcNow;

            await _store.UpdateAsync(model, ct: cancellationToken).ConfigureAwait(false);
            return true;
        }

        public async Task<JobDescriptor?> GetAsync(Guid jobId, CancellationToken cancellationToken = default)
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                return await GetCoreAsync(jobId, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _gate.Release();
            }
        }

        private async Task<JobDescriptor?> GetCoreAsync(Guid jobId, CancellationToken cancellationToken)
        {
            var model = await _store.ReadAsync(j => j.Guid == jobId, cancellationToken).ConfigureAwait(false);
            return model?.ToDescriptor();
        }

        public async Task<IReadOnlyList<JobDescriptor>> GetByStatusAsync(JobStatus status, int limit = 100, CancellationToken cancellationToken = default)
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                return await GetByStatusCoreAsync(status, limit, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _gate.Release();
            }
        }

        private async Task<IReadOnlyList<JobDescriptor>> GetByStatusCoreAsync(JobStatus status, int limit, CancellationToken cancellationToken)
        {
            var statusInt = (int)status;

            var models = await _store.ReadAsync(
                filter: j => j.Status == statusInt,
                orderBy: OrderBy<XmlJobDescriptorModel>.ByDescending(j => j.EnqueuedAt),
                limit: limit,
                ct: cancellationToken
            ).ConfigureAwait(false);

            return models.Select(m => m.ToDescriptor()).ToList();
        }

        public async Task<int> PurgeAsync(TimeSpan olderThan, CancellationToken cancellationToken = default)
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                return await PurgeCoreAsync(olderThan, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _gate.Release();
            }
        }

        private async Task<int> PurgeCoreAsync(TimeSpan olderThan, CancellationToken cancellationToken)
        {
            var cutoff = _clock.UtcNow.Subtract(olderThan);
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
