using System;
using System.Threading;
using System.Threading.Tasks;
using Birko.Redis;
using StackExchange.Redis;

namespace Birko.BackgroundJobs.Redis
{
    /// <summary>
    /// Provides distributed locking using Redis for job queue coordination.
    /// Prevents multiple workers from processing the same jobs simultaneously.
    /// Uses Redis SET NX with expiry (Redlock single-instance pattern), <b>renewed on a heartbeat</b>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Redis cannot express a session-scoped lock: there is no server-side notion of "release this when
    /// that client goes away". The nearest honest approximation is a short lease that this provider
    /// <b>renews while it is alive</b> - if the process dies the renewals stop, the key expires on its own,
    /// and the lock frees without anyone having to notice. That is what lets leader election survive a
    /// crashed leader here, and it is why <see cref="IsLeaseBased"/> is <c>true</c>: the guarantee is
    /// strictly weaker than a SQL advisory lock's.
    /// </para>
    /// <para>
    /// Before TASK-232 the caller's <c>timeout</c> was passed straight through as the key's expiry with no
    /// renewal, so the lock silently expired mid-work on any run longer than that value - releasing while
    /// the holder was still going, which is the failure mutual exclusion exists to prevent. The lease is
    /// now short by design (<see cref="DefaultLeaseDuration"/>) precisely because it is renewed; a long
    /// unrenewed lease only makes the same failure slower.
    /// </para>
    /// <para>
    /// A renewal can still be lost to a long GC pause, a network stall or a Redis failover. Work that must
    /// not run twice has to be idempotent regardless - see <see cref="IJobLockProvider"/>.
    /// </para>
    /// </remarks>
    public class RedisJobLockProvider : IJobLockProvider
    {
        private readonly RedisConnectionManager _connectionManager;
        private readonly RedisSettings _settings;
        private readonly bool _ownsConnection;
        private string? _lockToken;
        private string? _lockKey;
        private bool _disposed;
        private CancellationTokenSource? _renewalCts;
        private Task? _renewalTask;
        private TimeSpan _lease;

        /// <summary>
        /// Lease used when the caller does not specify one. Short on purpose: it is renewed on a heartbeat,
        /// so its only job is to bound how long a dead holder's lock lingers.
        /// </summary>
        public static readonly TimeSpan DefaultLeaseDuration = TimeSpan.FromSeconds(30);

        /// <summary>Fraction of the lease after which a renewal is attempted.</summary>
        private const double RenewAtFractionOfLease = 0.5;

        private string KeyPrefix => _settings.KeyPrefix ?? "birko:jobs";

        // Atomic check-and-delete: only delete the key if it still holds our token, so we never release
        // someone else's lock. Single definition shared by ReleaseAsync/DisposeAsync/Dispose (CR-L031).
        private const string SafeReleaseScript = @"
                if redis.call('GET', KEYS[1]) == ARGV[1] then
                    return redis.call('DEL', KEYS[1])
                else
                    return 0
                end
            ";

        private Task SafeReleaseAsync()
        {
            var db = _connectionManager.GetDatabase();
            return db.ScriptEvaluateAsync(SafeReleaseScript, new RedisKey[] { _lockKey! }, new RedisValue[] { _lockToken! });
        }

        private void SafeReleaseSync()
        {
            var db = _connectionManager.GetDatabase();
            db.ScriptEvaluate(SafeReleaseScript, new RedisKey[] { _lockKey! }, new RedisValue[] { _lockToken! });
        }

        // Extend the lease only while the key still holds OUR token, so a provider whose lease already
        // expired and was taken by someone else cannot resurrect its claim.
        private const string SafeRenewScript = @"
                if redis.call('GET', KEYS[1]) == ARGV[1] then
                    return redis.call('PEXPIRE', KEYS[1], ARGV[2])
                else
                    return 0
                end
            ";

        private async Task RenewLoopAsync(CancellationToken ct)
        {
            var every = TimeSpan.FromMilliseconds(_lease.TotalMilliseconds * RenewAtFractionOfLease);
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(every, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }

                try
                {
                    var db = _connectionManager.GetDatabase();
                    var kept = (long)await db.ScriptEvaluateAsync(
                        SafeRenewScript,
                        new RedisKey[] { _lockKey! },
                        new RedisValue[] { _lockToken!, (long)_lease.TotalMilliseconds }).ConfigureAwait(false);

                    // 0 means the key is gone, or now holds another token: the lease was lost. Stop
                    // claiming to hold it rather than renewing something we no longer own.
                    if (kept == 0)
                    {
                        IsLocked = false;
                        return;
                    }
                }
                catch
                {
                    // A failed renewal is not fatal by itself - the lease has not expired yet and the next
                    // tick may succeed. Swallowing beats tearing the loop down on one blip.
                }
            }
        }

        private void StopRenewal()
        {
            try { _renewalCts?.Cancel(); } catch { /* already disposed */ }
            _renewalCts?.Dispose();
            _renewalCts = null;
            _renewalTask = null;
        }

        /// <summary>
        /// Whether a lock is currently held.
        /// </summary>
        public bool IsLocked { get; private set; }

        /// <summary>
        /// Always true: Redis has no session-scoped lock, so this provider offers a renewed lease.
        /// </summary>
        public bool IsLeaseBased => true;

        /// <summary>
        /// Creates a lock provider using connection settings.
        /// </summary>
        public RedisJobLockProvider(RedisSettings settings)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _connectionManager = new RedisConnectionManager(settings);
            _ownsConnection = true;
        }

        /// <summary>
        /// Creates a lock provider using an existing connection manager.
        /// </summary>
        public RedisJobLockProvider(RedisConnectionManager connectionManager, RedisSettings settings)
        {
            _connectionManager = connectionManager ?? throw new ArgumentNullException(nameof(connectionManager));
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _ownsConnection = false;
        }

        /// <summary>
        /// Attempts to acquire a named distributed lock, retrying until
        /// <paramref name="acquireTimeout"/> elapses. Returns true if acquired.
        /// </summary>
        /// <remarks>
        /// <c>SET NX</c> does not block, so the wait is a poll - a real cost of this backend, stated rather
        /// than hidden. <paramref name="leaseDuration"/> defaults to <see cref="DefaultLeaseDuration"/>;
        /// whatever it is, a heartbeat renews it at half its length while this provider lives, so the lock
        /// survives long work and expires only once the holder is gone.
        /// </remarks>
        public async Task<bool> TryAcquireAsync(
            string lockName,
            TimeSpan acquireTimeout,
            TimeSpan? leaseDuration = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (IsLocked)
            {
                return true;
            }

            _lease = leaseDuration ?? DefaultLeaseDuration;
            if (_lease <= TimeSpan.Zero)
            {
                throw new ArgumentException("A lease duration must be positive.", nameof(leaseDuration));
            }

            var db = _connectionManager.GetDatabase();
            _lockKey = $"{KeyPrefix}:lock:{lockName}";
            _lockToken = Guid.NewGuid().ToString();

            var deadline = DateTime.UtcNow.Add(acquireTimeout);
            var backoff = TimeSpan.FromMilliseconds(50);
            bool acquired;
            while (true)
            {
                acquired = await db.StringSetAsync(_lockKey, _lockToken, _lease, When.NotExists)
                                   .ConfigureAwait(false);
                if (acquired || DateTime.UtcNow >= deadline)
                {
                    break;
                }

                cancellationToken.ThrowIfCancellationRequested();
                await Task.Delay(backoff, cancellationToken).ConfigureAwait(false);
                if (backoff < TimeSpan.FromMilliseconds(500))
                {
                    backoff += TimeSpan.FromMilliseconds(50);
                }
            }

            IsLocked = acquired;
            if (acquired)
            {
                _renewalCts = new CancellationTokenSource();
                _renewalTask = RenewLoopAsync(_renewalCts.Token);
            }
            else
            {
                _lockKey = null;
                _lockToken = null;
            }
            return acquired;
        }

        /// <summary>
        /// Releases the distributed lock. Only releases if the lock token matches (safe release).
        /// </summary>
        public async Task ReleaseAsync(string lockName, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!IsLocked || _lockKey == null || _lockToken == null)
            {
                return;
            }

            StopRenewal();
            await SafeReleaseAsync().ConfigureAwait(false);

            IsLocked = false;
            _lockKey = null;
            _lockToken = null;
        }

        public async ValueTask DisposeAsync()
        {
            if (!_disposed)
            {
                _disposed = true;
                StopRenewal();
                if (IsLocked && _lockKey != null && _lockToken != null)
                {
                    try
                    {
                        await SafeReleaseAsync().ConfigureAwait(false);
                    }
                    catch
                    {
                        // Best effort on dispose
                    }
                }
                IsLocked = false;
                if (_ownsConnection)
                {
                    _connectionManager.Dispose();
                }
            }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                StopRenewal();
                if (IsLocked && _lockKey != null && _lockToken != null)
                {
                    try
                    {
                        SafeReleaseSync();
                    }
                    catch
                    {
                        // Best effort on dispose
                    }
                }
                IsLocked = false;
                if (_ownsConnection)
                {
                    _connectionManager.Dispose();
                }
            }
        }
    }
}
