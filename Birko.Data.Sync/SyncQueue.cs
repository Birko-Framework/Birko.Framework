using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Birko.Data.Sync;

/// <summary>
/// Queue for managing concurrent synchronization operations
/// Ensures only one sync operation runs per scope at a time
/// </summary>
public class SyncQueue
{
    private readonly SemaphoreSlim _semaphore;
    private readonly Dictionary<string, Queue<QueuedSync>> _queues = new();
    // SH-M051 (TASK-157): one lock per queue key, held for the whole run. The class promised one sync per scope at a time,
    // but the only gate was the shared semaphore, so with maxConcurrentSyncs > 1 two runs of ONE scope ran together
    // against the same knowledge rows. Reference-counted, so a key with no waiter holds no lock object.
    private readonly Dictionary<string, (SemaphoreSlim Gate, int Users)> _scopeGates = new();
    private readonly object _lock = new();
    private readonly int _maxConcurrentSyncs;

    /// <summary>
    /// Maximum number of concurrent syncs (default: 1)
    /// </summary>
    public int MaxConcurrentSyncs => _maxConcurrentSyncs;

    /// <summary>
    /// Create a new sync queue
    /// </summary>
    public SyncQueue(int maxConcurrentSyncs = 1)
    {
        _maxConcurrentSyncs = maxConcurrentSyncs;
        _semaphore = new SemaphoreSlim(_maxConcurrentSyncs, _maxConcurrentSyncs);
    }

    /// <summary>
    /// Enqueue and execute a sync operation
    /// </summary>
    public async Task<T> EnqueueAsync<T>(
        string scope,
        Func<Task<T>> syncOperation,
        CancellationToken cancellationToken = default)
    {
        var key = GetQueueKey(scope);
        return await EnqueueWithKeyAsync<T>(key, syncOperation, cancellationToken);
    }

    /// <summary>
    /// Enqueue and execute a sync operation with a specific queue key
    /// </summary>
    protected async Task<T> EnqueueWithKeyAsync<T>(
        string queueKey,
        Func<Task<T>> syncOperation,
        CancellationToken cancellationToken)
    {
        var queuedSync = new QueuedSync
        {
            Key = queueKey,
            EnqueuedAt = DateTime.UtcNow
        };

        // Add to queue
        EnqueueInternal(queueKey, queuedSync);

        var scopeGate = AcquireScopeGate(queueKey);
        var holdsScope = false;
        var holdsPermit = false;
        try
        {
            // The scope first, then a permit: a run waiting behind its own scope must not hold one of the shared permits.
            await scopeGate.WaitAsync(cancellationToken);
            holdsScope = true;
            await _semaphore.WaitAsync(cancellationToken);
            holdsPermit = true;

            RemoveQueued(queueKey, queuedSync);
            queuedSync.StartedAt = DateTime.UtcNow;
            var result = await syncOperation();
            queuedSync.CompletedAt = DateTime.UtcNow;
            return result;
        }
        finally
        {
            // SH-M050 (TASK-157): release only what was acquired. A cancelled wait used to release a permit it never
            // took, inflating the count past maxConcurrentSyncs (or throwing SemaphoreFullException), and its queue
            // record stayed, so GetQueueLength counted it forever.
            if (queuedSync.StartedAt == null)
            {
                RemoveQueued(queueKey, queuedSync);
            }
            if (holdsPermit)
            {
                _semaphore.Release();
            }
            if (holdsScope)
            {
                scopeGate.Release();
            }
            ReleaseScopeGate(queueKey);
        }
    }

    private SemaphoreSlim AcquireScopeGate(string queueKey)
    {
        lock (_lock)
        {
            var entry = _scopeGates.TryGetValue(queueKey, out var existing) ? existing : (Gate: new SemaphoreSlim(1, 1), Users: 0);
            _scopeGates[queueKey] = (entry.Gate, entry.Users + 1);
            return entry.Gate;
        }
    }

    private void ReleaseScopeGate(string queueKey)
    {
        lock (_lock)
        {
            if (!_scopeGates.TryGetValue(queueKey, out var entry))
            {
                return;
            }
            if (entry.Users <= 1)
            {
                _scopeGates.Remove(queueKey);
                entry.Gate.Dispose();
                return;
            }
            _scopeGates[queueKey] = (entry.Gate, entry.Users - 1);
        }
    }

    /// <summary>
    /// Removes one specific record from its key's queue: the run that started, or a waiter that was cancelled. Removing
    /// the head instead attributed the start to whichever caller enqueued first, not to the one that got the gate.
    /// </summary>
    private void RemoveQueued(string queueKey, QueuedSync queuedSync)
    {
        lock (_lock)
        {
            if (!_queues.TryGetValue(queueKey, out var queue))
            {
                return;
            }
            var remaining = new Queue<QueuedSync>(queue.Where(q => !ReferenceEquals(q, queuedSync)));
            if (remaining.Count == 0)
            {
                _queues.Remove(queueKey);
            }
            else
            {
                _queues[queueKey] = remaining;
            }
        }
    }

    /// <summary>
    /// Add a sync operation to the queue
    /// </summary>
    protected void EnqueueInternal(string queueKey, QueuedSync queuedSync)
    {
        lock (_lock)
        {
            if (!_queues.ContainsKey(queueKey))
            {
                _queues[queueKey] = new Queue<QueuedSync>();
            }
            _queues[queueKey].Enqueue(queuedSync);
        }
    }

    /// <summary>
    /// Dequeue the next sync operation
    /// </summary>
    protected QueuedSync? DequeueNext(string queueKey)
    {
        lock (_lock)
        {
            if (_queues.TryGetValue(queueKey, out var queue) && queue.Count > 0)
            {
                var currentSync = queue.Dequeue();
                if (queue.Count == 0)
                {
                    _queues.Remove(queueKey);
                }
                return currentSync;
            }
            return null;
        }
    }

    /// <summary>
    /// Get queue key for scope
    /// </summary>
    protected virtual string GetQueueKey(string scope)
    {
        return scope;
    }

    /// <summary>
    /// Get queue key for scope with optional tenant
    /// </summary>
    protected virtual string GetQueueKey(string scope, Guid? tenantId)
    {
        return tenantId.HasValue ? $"{scope}_{tenantId.Value}" : scope;
    }

    /// <summary>
    /// Get the lock object for thread safety
    /// </summary>
    protected object Lock => _lock;

    /// <summary>
    /// Get the queues dictionary
    /// </summary>
    protected Dictionary<string, Queue<QueuedSync>> Queues => _queues;

    /// <summary>
    /// Get the semaphore for concurrency control
    /// </summary>
    protected SemaphoreSlim Semaphore => _semaphore;

    /// <summary>
    /// Get the number of queued operations for a scope
    /// </summary>
    public int GetQueueLength(string scope)
    {
        var key = GetQueueKey(scope);
        lock (_lock)
        {
            return _queues.TryGetValue(key, out var queue) ? queue.Count : 0;
        }
    }

    /// <summary>
    /// Get all queue lengths
    /// </summary>
    public Dictionary<string, int> GetAllQueueLengths()
    {
        lock (_lock)
        {
            return _queues.ToDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value.Count
            );
        }
    }

    /// <summary>
    /// Clear all queues
    /// </summary>
    public void Clear()
    {
        lock (_lock)
        {
            _queues.Clear();
        }
    }

    /// <summary>
    /// Represents a queued sync operation
    /// </summary>
    protected class QueuedSync
    {
        public string Key { get; set; } = string.Empty;
        public DateTime EnqueuedAt { get; set; }
        public DateTime? StartedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
    }
}
