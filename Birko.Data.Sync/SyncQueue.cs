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

        try
        {
            // Wait for semaphore (limit concurrent operations)
            await _semaphore.WaitAsync(cancellationToken);

            // Remove from queue and execute
            var currentSync = DequeueNext(queueKey);

            if (currentSync != null)
            {
                currentSync.StartedAt = DateTime.UtcNow;
                var result = await syncOperation();
                currentSync.CompletedAt = DateTime.UtcNow;
                return result;
            }

            // Shouldn't reach here, but if we do, run the operation
            return await syncOperation();
        }
        finally
        {
            _semaphore.Release();
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
