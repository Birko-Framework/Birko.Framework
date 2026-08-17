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
    /// Both <c>SqlJobLockProvider&lt;DB&gt;</c> and <c>RedisJobLockProvider</c> already implemented these
    /// members with these exact signatures; they simply shared no interface, so a caller could not accept
    /// "a lock provider" without also choosing a backend. Declaring it changes no behaviour — it makes the
    /// existing similarity substitutable.
    /// </para>
    /// <para>
    /// <b>Locks are session-scoped, not lease-scoped.</b> An implementation holds the lock until it is
    /// released or the provider is disposed, and the backend is expected to release it if the holder dies
    /// — a SQL advisory lock on a dedicated connection drops when the connection does. That is what makes
    /// leader election safe against a crashed leader without a heartbeat.
    /// </para>
    /// <para>
    /// ⚠ <b>A caller must not assume it still holds a lock it acquired earlier.</b> Connections drop and
    /// backends restart, so <see cref="IsLocked"/> is a statement about this provider's belief, not a
    /// guarantee about the world. Work that must not run twice needs to be idempotent regardless; a lock
    /// reduces duplication, it does not abolish it.
    /// </para>
    /// </remarks>
    public interface IJobLockProvider : IAsyncDisposable, IDisposable
    {
        /// <summary>Whether this provider currently believes it holds a lock.</summary>
        bool IsLocked { get; }

        /// <summary>
        /// Attempts to acquire the named lock, waiting up to <paramref name="timeout"/>. Returns
        /// <c>false</c> rather than throwing when the lock is held elsewhere — losing the race is an
        /// expected outcome, not an error.
        /// </summary>
        Task<bool> TryAcquireAsync(string lockName, TimeSpan timeout, CancellationToken cancellationToken = default);

        /// <summary>Releases the named lock. Releasing a lock that is not held is not an error.</summary>
        Task ReleaseAsync(string lockName, CancellationToken cancellationToken = default);
    }
}
