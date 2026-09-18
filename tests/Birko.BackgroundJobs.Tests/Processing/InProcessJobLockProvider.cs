using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Birko.BackgroundJobs;

namespace Birko.BackgroundJobs.Tests.Processing
{
    /// <summary>
    /// An <see cref="IJobLockProvider"/> giving real mutual exclusion between provider instances inside one
    /// process, so leader election can be proven without a server.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Needed because neither shipped provider can stand in here: <c>SqlJobLockProvider</c> returns
    /// <c>false</c> on SQLite (there is no portable cross-connection advisory lock), which would make both
    /// schedulers followers and turn "one enqueue not two" into "zero enqueues" — a test that passes for the
    /// wrong reason. The live halves of this proof run against Redis and PostgreSQL in their own suites.
    /// </para>
    /// <para>
    /// The registry is static because the point is exclusion <i>between instances</i>; every test therefore
    /// uses a unique lock name.
    /// </para>
    /// </remarks>
    public sealed class InProcessJobLockProvider : IJobLockProvider
    {
        private static readonly ConcurrentDictionary<string, InProcessJobLockProvider> Held = new();

        private string? _heldName;
        private bool _disposed;

        public InProcessJobLockProvider(bool leaseBased = false)
        {
            IsLeaseBased = leaseBased;
        }

        public bool IsLocked { get; private set; }

        public bool IsLeaseBased { get; }

        /// <summary>How many times acquisition was attempted — pins that a follower keeps knocking.</summary>
        public int AcquireAttempts;

        /// <summary>When set, the next acquisition attempt throws instead of answering.</summary>
        public bool FailAcquire { get; set; }

        /// <summary>
        /// When set, acquisition cancels this source and then observes the cancellation — reproducing the
        /// one window in which the scheduler's loop can be cancelled *inside* an acquire rather than inside
        /// its own delay. Narrow, and the only way that path is deterministically reachable.
        /// </summary>
        public CancellationTokenSource? CancelDuringAcquire { get; set; }

        public Task<bool> TryAcquireAsync(
            string lockName,
            TimeSpan acquireTimeout,
            TimeSpan? leaseDuration = null,
            CancellationToken cancellationToken = default)
        {
            CancelDuringAcquire?.Cancel();
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref AcquireAttempts);

            if (FailAcquire)
            {
                throw new InvalidOperationException("lock backend is down");
            }

            if (IsLocked)
            {
                return Task.FromResult(true);
            }

            if (!Held.TryAdd(lockName, this))
            {
                return Task.FromResult(false);
            }

            _heldName = lockName;
            IsLocked = true;
            return Task.FromResult(true);
        }

        public Task ReleaseAsync(string lockName, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Free();
            return Task.CompletedTask;
        }

        /// <summary>
        /// Drops the lock the way a lease expires: without the holder asking, and without telling it
        /// anything beyond <see cref="IsLocked"/> turning false. This is what
        /// <c>RedisJobLockProvider</c>'s renewal loop does when it finds the key gone.
        /// </summary>
        public void SimulateLeaseLoss() => Free();

        private void Free()
        {
            if (_heldName != null)
            {
                Held.TryRemove(new KeyValuePair<string, InProcessJobLockProvider>(_heldName, this));
                _heldName = null;
            }
            IsLocked = false;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Free();
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return default;
        }
    }
}
