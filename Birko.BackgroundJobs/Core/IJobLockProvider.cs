using System;
using System.Threading;
using System.Threading.Tasks;

namespace Birko.BackgroundJobs
{
    /// <summary>
    /// Coordinates work across processes with named, mutually exclusive locks.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A durable <see cref="IJobQueue"/> lets several workers share one store, which makes claiming a job
    /// safe — the queue's own dequeue is atomic. It does not make <i>deciding to enqueue</i> safe: a
    /// recurring schedule lives in each process's memory, so every worker independently concludes that a
    /// job is due and enqueues its own copy. This is the contract for the coordination that prevents that,
    /// whether the answer is leader election or a lock around an individual decision.
    /// </para>
    /// <para>
    /// <b>Two independent durations, deliberately separate parameters.</b> The first version of this
    /// interface had a single <c>timeout</c>, and the two shipped implementations read it as two different
    /// things: SQL used it to decide <i>how long to wait for the lock</i>, while Redis passed it straight
    /// into <c>SET … NX EX</c> as the key's <i>expiry</i>. The same call therefore meant "wait five
    /// minutes, then hold indefinitely" on one backend and "do not wait, and drop the lock in five
    /// minutes" on the other — with PostgreSQL ignoring it entirely, because
    /// <c>pg_try_advisory_lock</c> does not block. One parameter cannot carry both meanings, and no
    /// amount of documentation makes it safe to try (TASK-232).
    /// </para>
    /// <para>
    /// <b>Session-scoped is the contract; a lease is a declared deviation.</b> An implementation is
    /// expected to hold the lock until it is released or the provider is disposed, and the backend is
    /// expected to release it if the holder <i>dies</i> — a SQL advisory lock on a dedicated connection
    /// drops when the connection does. That is what makes leader election safe against a crashed leader
    /// with no heartbeat. Where a backend can only express a time-bounded lease, it must say so through
    /// <see cref="IsLeaseBased"/> rather than pretending otherwise, because the two fail in opposite
    /// directions: a session lock's failure is a <i>stuck</i> lock nobody can take over, while a lease's
    /// failure is <b>releasing while the holder is still working</b> — which is the very thing mutual
    /// exclusion exists to prevent.
    /// </para>
    /// <para>
    /// ⚠ <b>A caller must not assume it still holds a lock it acquired earlier.</b> Connections drop and
    /// backends restart, so <see cref="IsLocked"/> is a statement about this provider's belief, not a
    /// guarantee about the world. Work that must not run twice needs to be idempotent regardless; a lock
    /// reduces duplication, it does not abolish it. On a lease-based provider this is not a corner case
    /// but the routine one — check <see cref="IsLeaseBased"/> and design accordingly.
    /// </para>
    /// </remarks>
    public interface IJobLockProvider : IAsyncDisposable, IDisposable
    {
        /// <summary>Whether this provider currently believes it holds a lock.</summary>
        bool IsLocked { get; }

        /// <summary>
        /// Whether this provider's locks are time-bounded <b>leases</b> rather than session-scoped locks.
        /// </summary>
        /// <remarks>
        /// <c>false</c> means the lock is held until released, disposed, or the holder dies — the contract
        /// described on this interface. <c>true</c> means the backend can only bound the lock by time, so
        /// it may expire <i>while this process is still working</i>, and two holders can briefly believe
        /// they own it. A caller that must not double-execute has to be idempotent on such a provider; one
        /// that only wants to reduce duplication can ignore this.
        /// <para>
        /// Exposed rather than hidden because the distinction changes what a caller must do, and hiding it
        /// behind a uniform interface is what made the original single-<c>timeout</c> design unsafe.
        /// </para>
        /// </remarks>
        bool IsLeaseBased { get; }

        /// <summary>
        /// Attempts to acquire the named lock, waiting up to <paramref name="acquireTimeout"/> for a
        /// current holder to release it. Returns <c>false</c> rather than throwing when the lock is held
        /// elsewhere — losing the race is an expected outcome, not an error.
        /// </summary>
        /// <param name="lockName">The lock's name. Callers sharing a name are mutually excluded.</param>
        /// <param name="acquireTimeout">
        /// How long to wait for the lock. <see cref="TimeSpan.Zero"/> means try once and return
        /// immediately. A provider whose backend offers only a non-blocking attempt
        /// (<c>pg_try_advisory_lock</c>) may return early regardless — it must not busy-wait to simulate
        /// blocking.
        /// </param>
        /// <param name="leaseDuration">
        /// How long the lock may be held before the backend is free to reclaim it. <c>null</c> requests a
        /// session-scoped lock with no expiry, which is the default and what a session-based provider
        /// always gives. A <see cref="IsLeaseBased"/> provider treats <c>null</c> as "use my own default"
        /// rather than "never expire", because it has no way to offer that.
        /// </param>
        /// <param name="cancellationToken">Cancels the wait.</param>
        Task<bool> TryAcquireAsync(
            string lockName,
            TimeSpan acquireTimeout,
            TimeSpan? leaseDuration = null,
            CancellationToken cancellationToken = default);

        /// <summary>Releases the named lock. Releasing a lock that is not held is not an error.</summary>
        Task ReleaseAsync(string lockName, CancellationToken cancellationToken = default);
    }
}
