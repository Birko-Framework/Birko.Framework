using System;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Birko.Data.SQL.Connectors;
using Birko.Data.Stores;
using Birko.Configuration;

namespace Birko.BackgroundJobs.SQL
{
    /// <summary>
    /// Provides distributed locking using SQL database advisory locks.
    /// Prevents multiple workers from processing the same jobs simultaneously.
    /// Uses a Birko.Data.SQL connector for connection creation.
    /// </summary>
    public class SqlJobLockProvider<DB> : IAsyncDisposable, IDisposable
        where DB : AbstractConnector
    {
        private readonly AbstractConnectorBase _connector;
        private readonly PasswordSettings _settings;
        private DbConnection? _lockConnection;
        private bool _disposed;

        /// <summary>
        /// Whether a lock is currently held.
        /// </summary>
        public bool IsLocked { get; private set; }

        /// <summary>
        /// Creates a lock provider using connection settings and a connector.
        /// </summary>
        public SqlJobLockProvider(PasswordSettings settings)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _connector = (AbstractConnectorBase)Data.SQL.DataBase.GetConnector<DB>(settings);
        }

        /// <summary>
        /// Attempts to acquire a named advisory lock. Returns true if acquired.
        /// The lock is held on a dedicated connection until released or disposed.
        /// </summary>
        public async Task<bool> TryAcquireAsync(string lockName, TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            if (IsLocked)
            {
                return true;
            }

            _lockConnection = _connector.CreateConnection(_settings);
            await _lockConnection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var lockKey = GetLockKey(lockName);

            using var cmd = _lockConnection.CreateCommand();
            cmd.CommandTimeout = (int)timeout.TotalSeconds + 1;
            cmd.CommandText = $"SELECT pg_try_advisory_lock({lockKey})";

            try
            {
                var result = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
                IsLocked = result is bool b && b;
                if (!IsLocked)
                {
                    await ReleaseConnectionAsync().ConfigureAwait(false);
                }
                return IsLocked;
            }
            catch (DbException)
            {
                // Not PostgreSQL — rely on transaction-level locking in dequeue
                await ReleaseConnectionAsync().ConfigureAwait(false);
                return true;
            }
        }

        /// <summary>
        /// Releases the advisory lock.
        /// </summary>
        public async Task ReleaseAsync(string lockName, CancellationToken cancellationToken = default)
        {
            if (!IsLocked || _lockConnection == null)
            {
                return;
            }

            var lockKey = GetLockKey(lockName);

            try
            {
                using var cmd = _lockConnection.CreateCommand();
                cmd.CommandText = $"SELECT pg_advisory_unlock({lockKey})";
                await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (DbException)
            {
                // Lock released when connection closes
            }

            IsLocked = false;
            await ReleaseConnectionAsync().ConfigureAwait(false);
        }

        private static long GetLockKey(string lockName)
        {
            unchecked
            {
                long hash = 5381;
                foreach (char c in lockName)
                {
                    hash = ((hash << 5) + hash) + c;
                }
                return hash & 0x7FFFFFFFFFFFFFFF;
            }
        }

        private async Task ReleaseConnectionAsync()
        {
            if (_lockConnection != null)
            {
                await _lockConnection.CloseAsync().ConfigureAwait(false);
                await _lockConnection.DisposeAsync().ConfigureAwait(false);
                _lockConnection = null;
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (!_disposed)
            {
                _disposed = true;
                IsLocked = false;
                await ReleaseConnectionAsync().ConfigureAwait(false);
            }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                IsLocked = false;
                _lockConnection?.Close();
                _lockConnection?.Dispose();
                _lockConnection = null;
            }
        }
    }
}
