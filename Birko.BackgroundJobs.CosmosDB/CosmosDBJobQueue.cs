using Birko.BackgroundJobs.CosmosDB.Models;
using Birko.Data.CosmosDB.Stores;
using Birko.Data.Stores;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Birko.BackgroundJobs.CosmosDB;

/// <summary>
/// Cosmos DB implementation of IJobQueue using AsyncCosmosDBStore.
/// </summary>
public class CosmosDBJobQueue : IJobQueue
{
    private readonly AsyncCosmosDBStore<CosmosJobDescriptorModel> _store;
    private readonly RetryPolicy _retryPolicy;

    /// <summary>How long a job may stay Processing before it is presumed abandoned (TASK-664; see <see cref="JobReclaim"/>).</summary>
    private readonly TimeSpan _claimTimeout;

    /// <summary>
    /// Gets the underlying store for transaction context access.
    /// </summary>
    public AsyncCosmosDBStore<CosmosJobDescriptorModel> Store => _store;

    /// <summary>
    /// Creates a new Cosmos DB job queue with settings.
    /// </summary>
    public CosmosDBJobQueue(Birko.Data.CosmosDB.Stores.Settings settings, RetryPolicy? retryPolicy = null, TimeSpan? claimTimeout = null)
    {
        _store = new AsyncCosmosDBStore<CosmosJobDescriptorModel>();
        _store.SetSettings(settings);
        _retryPolicy = retryPolicy ?? RetryPolicy.Default;
        _claimTimeout = JobReclaim.Resolve(claimTimeout);
    }

    /// <summary>
    /// Creates a new Cosmos DB job queue with an existing store.
    /// </summary>
    public CosmosDBJobQueue(AsyncCosmosDBStore<CosmosJobDescriptorModel> store, RetryPolicy? retryPolicy = null, TimeSpan? claimTimeout = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _retryPolicy = retryPolicy ?? RetryPolicy.Default;
        _claimTimeout = JobReclaim.Resolve(claimTimeout);
    }

    /// <inheritdoc />
    public async Task<Guid> EnqueueAsync(JobDescriptor descriptor, CancellationToken ct = default)
    {
        var model = CosmosJobDescriptorModel.FromDescriptor(descriptor);
        var id = await _store.CreateAsync(model, ct: ct).ConfigureAwait(false);
        return id;
    }

    /// <inheritdoc />
    public async Task<JobDescriptor?> DequeueAsync(string? queueName = null, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        // TASK-664: a job left Processing longer than the claim timeout is a candidate too, as on SQL. The compare-and-set
        // re-checks staleness on the stored item, and the winner stamps LastAttemptAt, so one worker reclaims it.
        var staleBefore = now - _claimTimeout;
        var results = await ReadCandidatesAsync(now, staleBefore, queueName, ct).ConfigureAwait(false);

        // TASK-661 (SH-M001): each candidate is claimed by a conditional write (ETag), re-checked on the item as stored.
        // It was read, mutated and replaced, so concurrent dequeuers all claimed the same job (measured on the emulator:
        // 6 workers, 6 jobs, 24 claims of 4 jobs). A lost race moves on to the next candidate.
        for (var round = 0; round < MaxClaimRounds; round++)
        {
            foreach (var candidate in results)
            {
                if (candidate.Status == (int)JobStatus.Processing && candidate.AttemptCount >= _retryPolicy.MaxRetriesFor(candidate.MaxRetries))
                {
                    // Abandoned with no attempts left: buried, under the same staleness condition, never claimed.
                    await _store.UpdateIfAsync(candidate.Guid!.Value, m => IsStale(m, staleBefore), m =>
                    {
                        m.Status = (int)JobStatus.Dead;
                        m.CompletedAt = now;
                        m.LastError = JobReclaim.AbandonedError(_claimTimeout, m.AttemptCount);
                    }, ct).ConfigureAwait(false);
                    continue;
                }

                var claimed = await _store.UpdateIfAsync(
                    candidate.Guid!.Value,
                    m => IsDue(m, now, staleBefore, queueName),
                    m =>
                    {
                        m.Status = (int)JobStatus.Processing;
                        m.LastAttemptAt = DateTime.UtcNow;
                        m.AttemptCount++;
                    },
                    ct).ConfigureAwait(false);
                if (claimed != null) return claimed.ToDescriptor();
            }

            if (results.Count < ClaimBatch) return null;
            results = await ReadCandidatesAsync(now, staleBefore, queueName, ct).ConfigureAwait(false);
            if (results.Count == 0) return null;
        }

        return null;
    }

    /// <summary>Candidates read per round; every one a round loses to another worker is skipped, not retried.</summary>
    private const int ClaimBatch = 8;

    /// <summary>Rounds of candidates one dequeue tries before it reports nothing to do.</summary>
    private const int MaxClaimRounds = 4;

    private static bool IsDue(CosmosJobDescriptorModel m, DateTime now, DateTime staleBefore, string? queueName)
        => (m.Status == (int)JobStatus.Pending || (m.Status == (int)JobStatus.Scheduled && m.ScheduledAt != null && m.ScheduledAt <= now)
                || IsStale(m, staleBefore))
            && (queueName == null || m.QueueName == null || m.QueueName == queueName);

    private static bool IsStale(CosmosJobDescriptorModel m, DateTime staleBefore)
        => m.Status == (int)JobStatus.Processing && m.LastAttemptAt != null && m.LastAttemptAt < staleBefore;

    /// <inheritdoc />
    public async Task CompleteAsync(Guid jobId, CancellationToken ct = default)
    {
        // TASK-661: only a claimed job completes, as on every backend since TASK-158.
        await _store.UpdateIfAsync(jobId, m => m.Status == (int)JobStatus.Processing, m =>
        {
            m.Status = (int)JobStatus.Completed;
            m.CompletedAt = DateTime.UtcNow;
        }, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task FailAsync(Guid jobId, string error, CancellationToken ct = default)
    {
        // TASK-661: only a claimed job fails; the retry decision reads the item as stored, not a stale copy.
        await _store.UpdateIfAsync(jobId, m => m.Status == (int)JobStatus.Processing, m =>
        {
            m.LastError = error;
            var now = DateTime.UtcNow;
            if (m.AttemptCount < _retryPolicy.MaxRetriesFor(m.MaxRetries))
            {
                m.Status = (int)JobStatus.Scheduled;
                m.ScheduledAt = now.Add(_retryPolicy.GetDelay(m.AttemptCount));
            }
            else
            {
                // Retries exhausted — terminal Dead status (CR-H008), not the retryable Failed.
                m.Status = (int)JobStatus.Dead;
                m.CompletedAt = now;
            }
        }, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<bool> CancelAsync(Guid jobId, CancellationToken ct = default)
    {
        // TASK-661 (SH-M002): only a job that has not started is cancelled. It read by id alone, so a running,
        // completed or dead job was marked Cancelled and the call answered true.
        var cancelled = await _store.UpdateIfAsync(jobId,
            m => m.Status == (int)JobStatus.Pending || m.Status == (int)JobStatus.Scheduled,
            m =>
            {
                m.Status = (int)JobStatus.Cancelled;
                m.CompletedAt = DateTime.UtcNow;
            }, ct).ConfigureAwait(false);
        return cancelled != null;
    }

    private async Task<List<CosmosJobDescriptorModel>> ReadCandidatesAsync(DateTime now, DateTime staleBefore, string? queueName, CancellationToken ct)
    {
        var pendingStatus = (int)JobStatus.Pending;
        var scheduledStatus = (int)JobStatus.Scheduled;
        var processingStatus = (int)JobStatus.Processing;
        var results = await _store.ReadAsync(
            // Guard ScheduledAt != null (a Scheduled row with a null ScheduledAt is malformed and the
            // null-comparison semantics under the Cosmos SQL translation aren't guaranteed to match
            // in-memory nullable-DateTime comparison), and add a FIFO tiebreaker so equal-priority jobs
            // dequeue in enqueue order — matching the MongoDB backend (CR-L020/L021).
            // An unnamed job belongs to every queue, as on every sibling backend: JobDispatcher leaves QueueName
            // null and BackgroundJobProcessor always dequeues a named queue, so without the middle term no
            // dispatcher-enqueued job ever ran on Cosmos (SH-H001). The serializer writes QueueName: null
            // explicitly, so `= null` matches the stored document.
            filter: j => (j.Status == pendingStatus || (j.Status == scheduledStatus && j.ScheduledAt != null && j.ScheduledAt <= now)
                    || (j.Status == processingStatus && j.LastAttemptAt != null && j.LastAttemptAt < staleBefore))
                && (queueName == null || j.QueueName == null || j.QueueName == queueName),
            orderBy: OrderBy<CosmosJobDescriptorModel>.ByDescending(j => j.Priority).ThenBy(j => j.EnqueuedAt),
            limit: ClaimBatch,
            ct: ct
        ).ConfigureAwait(false);
        return results.ToList();
    }

    /// <inheritdoc />
    public async Task<JobDescriptor?> GetAsync(Guid jobId, CancellationToken ct = default)
    {
        var model = await _store.ReadAsync(jobId, ct).ConfigureAwait(false);
        return model?.ToDescriptor();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<JobDescriptor>> GetByStatusAsync(JobStatus status, int limit = 100, CancellationToken ct = default)
    {
        var statusInt = (int)status;
        var results = await _store.ReadAsync(
            filter: j => j.Status == statusInt,
            orderBy: OrderBy<CosmosJobDescriptorModel>.ByName(nameof(CosmosJobDescriptorModel.EnqueuedAt), descending: true),
            limit: limit,
            ct: ct
        ).ConfigureAwait(false);

        return results.Select(m => m.ToDescriptor()).ToList();
    }

    /// <inheritdoc />
    public async Task<int> PurgeAsync(TimeSpan olderThan, CancellationToken ct = default)
    {
        var cutoff = DateTime.UtcNow - olderThan;
        var completedStatus = (int)JobStatus.Completed;
        var deadStatus = (int)JobStatus.Dead;
        var cancelledStatus = (int)JobStatus.Cancelled;

        // Purge terminal jobs only: Completed | Dead | Cancelled (CR-H009). Failed is retryable.
        var results = await _store.ReadAsync(
            filter: j => (j.Status == completedStatus || j.Status == deadStatus || j.Status == cancelledStatus)
                && j.CompletedAt != null && j.CompletedAt < cutoff,
            ct: ct
        ).ConfigureAwait(false);

        var count = results.Count();
        if (count > 0)
        {
            await _store.DeleteAsync(results, ct).ConfigureAwait(false);
        }
        return count;
    }
}
