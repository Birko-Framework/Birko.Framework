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
    /// Uses Redis SET NX with expiry (Redlock single-instance pattern).
    /// </summary>
    public class RedisJobLockProvider : IAsyncDisposable, IDisposable
    {
        private readonly RedisConnectionManager _connectionManager;
        private readonly RedisSettings _settings;
        private readonly bool _ownsConnection;
        private string? _lockToken;
        private string? _lockKey;
        private bool _disposed;

        private string KeyPrefix => _settings.KeyPrefix ?? "birko:jobs";

        /// <summary>
        /// Whether a lock is currently held.
        /// </summary>
        public bool IsLocked { get; private set; }

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
        /// Attempts to acquire a named distributed lock. Returns true if acquired.
        /// The lock is held until released or until the timeout expires.
        /// </summary>
        public async Task<bool> TryAcquireAsync(string lockName, TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            if (IsLocked)
            {
                return true;
            }

            var db = _connectionManager.GetDatabase();
            _lockKey = $"{KeyPrefix}:lock:{lockName}";
            _lockToken = Guid.NewGuid().ToString();

            var acquired = await db.StringSetAsync(
                _lockKey,
                _lockToken,
                timeout,
                When.NotExists
            ).ConfigureAwait(false);

            IsLocked = acquired;
            return acquired;
        }

        /// <summary>
        /// Releases the distributed lock. Only releases if the lock token matches (safe release).
        /// </summary>
        public async Task ReleaseAsync(string lockName, CancellationToken cancellationToken = default)
        {
            if (!IsLocked || _lockKey == null || _lockToken == null)
            {
                return;
            }

            var db = _connectionManager.GetDatabase();

            // Atomic check-and-delete via Lua to avoid releasing someone else's lock
            const string script = @"
                if redis.call('GET', KEYS[1]) == ARGV[1] then
                    return redis.call('DEL', KEYS[1])
                else
                    return 0
                end
            ";

            await db.ScriptEvaluateAsync(
                script,
                new RedisKey[] { _lockKey },
                new RedisValue[] { _lockToken }
            ).ConfigureAwait(false);

            IsLocked = false;
            _lockKey = null;
            _lockToken = null;
        }

        public async ValueTask DisposeAsync()
        {
            if (!_disposed)
            {
                _disposed = true;
                if (IsLocked && _lockKey != null && _lockToken != null)
                {
                    try
                    {
                        var db = _connectionManager.GetDatabase();
                        const string script = @"
                            if redis.call('GET', KEYS[1]) == ARGV[1] then
                                return redis.call('DEL', KEYS[1])
                            else
                                return 0
                            end
                        ";
                        await db.ScriptEvaluateAsync(
                            script,
                            new RedisKey[] { _lockKey },
                            new RedisValue[] { _lockToken }
                        ).ConfigureAwait(false);
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
                if (IsLocked && _lockKey != null && _lockToken != null)
                {
                    try
                    {
                        var db = _connectionManager.GetDatabase();
                        const string script = @"
                            if redis.call('GET', KEYS[1]) == ARGV[1] then
                                return redis.call('DEL', KEYS[1])
                            else
                                return 0
                            end
                        ";
                        db.ScriptEvaluate(
                            script,
                            new RedisKey[] { _lockKey },
                            new RedisValue[] { _lockToken }
                        );
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
