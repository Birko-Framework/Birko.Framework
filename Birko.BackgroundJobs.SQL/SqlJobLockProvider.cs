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
    public class SqlJobLockProvider<DB> : IJobLockProvider
        where DB : AbstractConnector
    {
        private readonly AbstractConnectorBase _connector;
        private readonly PasswordSettings _settings;
        private DbConnection? _lockConnection;
        private bool _disposed;

        // TASK-158 (SH-M006): the name of the lock held. A provider holds one lock; it answered "true" for any other name
        // while holding one, and released by whatever name it was given.
        private string? _heldName;

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
        /// Session-scoped, not lease-based: the lock lives on a dedicated connection, so the server
        /// releases it if this process dies. <see cref="IJobLockProvider.IsLeaseBased"/> is false.
        /// </summary>
        public bool IsLeaseBased => false;

        /// <summary>
        /// Attempts to acquire a named advisory lock. Returns true if acquired.
        /// The lock is held on a dedicated connection until released or disposed.
        /// </summary>
        /// <remarks>
        /// <paramref name="acquireTimeout"/> is the wait for a current holder, which is what MSSql's
        /// <c>@LockTimeout</c> and MySQL's <c>GET_LOCK</c> take. PostgreSQL's <c>pg_try_advisory_lock</c>
        /// does not block at all, so it returns immediately whatever is passed — documented rather than
        /// simulated, because busy-waiting to fake blocking would burn a connection and a core to no
        /// benefit.
        /// <para>
        /// <paramref name="leaseDuration"/> must be <c>null</c>. A SQL advisory lock has no expiry: it is
        /// released explicitly or when the connection drops. Accepting a duration and silently ignoring it
        /// would promise a bound this provider cannot enforce, so it throws instead (TASK-232).
        /// </para>
        /// </remarks>
        public async Task<bool> TryAcquireAsync(
            string lockName,
            TimeSpan acquireTimeout,
            TimeSpan? leaseDuration = null,
            CancellationToken cancellationToken = default)
        {
            if (leaseDuration.HasValue)
            {
                throw new ArgumentException(
                    "SQL advisory locks are session-scoped and cannot expire, so a lease duration cannot be " +
                    "honoured. Pass null for a session lock, or use a provider whose IsLeaseBased is true.",
                    nameof(leaseDuration));
            }

            ArgumentException.ThrowIfNullOrWhiteSpace(lockName);
            if (IsLocked)
            {
                if (_heldName == lockName)
                {
                    return true;
                }
                throw new InvalidOperationException(
                    $"This provider holds the lock '{_heldName}' and cannot take '{lockName}' as well: a provider holds one " +
                    "lock at a time. Use a provider per lock.");
            }

            var dialect = Dialect;
            if (dialect == SqlDialect.Other)
            {
                // No portable cross-connection advisory lock (e.g. SQLite). Report failure so callers
                // fall back deliberately, instead of the previous behaviour of returning true for a
                // lock that was never taken (CR-M027).
                return false;
            }

            // CreateConnection/OpenAsync are inside the try so any failure routes through
            // ReleaseConnectionAsync and nulls _lockConnection instead of leaking it (CR-M026).
            try
            {
                _lockConnection = _connector.CreateConnection(_settings);
                await _lockConnection.OpenAsync(cancellationToken).ConfigureAwait(false);

                using var cmd = _lockConnection.CreateCommand();
                cmd.CommandTimeout = (int)acquireTimeout.TotalSeconds + 1;

                switch (dialect)
                {
                    case SqlDialect.PostgreSql:
                        cmd.CommandText = $"SELECT pg_try_advisory_lock({GetLockKey(lockName)})";
                        var pg = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
                        IsLocked = pg is bool b && b;
                        break;

                    case SqlDialect.MSSql:
                        // sp_getapplock returns its status via the RETURN value (>= 0 == granted).
                        cmd.CommandText = "DECLARE @r int; EXEC @r = sp_getapplock @Resource = @res, @LockMode = 'Exclusive', @LockOwner = 'Session', @LockTimeout = @to; SELECT @r;";
                        AddParam(cmd, "@res", lockName);
                        AddParam(cmd, "@to", (int)acquireTimeout.TotalMilliseconds);
                        var ms = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
                        IsLocked = ms != null && ms != DBNull.Value && Convert.ToInt32(ms) >= 0;
                        break;

                    case SqlDialect.MySql:
                        // GET_LOCK returns 1 on success, 0 on timeout, NULL on error.
                        cmd.CommandText = "SELECT GET_LOCK(@res, @to)";
                        AddParam(cmd, "@res", lockName);
                        AddParam(cmd, "@to", (int)acquireTimeout.TotalSeconds);
                        var my = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
                        IsLocked = my != null && my != DBNull.Value && Convert.ToInt64(my) == 1;
                        break;
                }

                if (!IsLocked)
                {
                    await ReleaseConnectionAsync().ConfigureAwait(false);
                }
                else
                {
                    _heldName = lockName;
                }
                return IsLocked;
            }
            catch
            {
                // Never leak the connection or report a lock that was not taken.
                IsLocked = false;
                await ReleaseConnectionAsync().ConfigureAwait(false);
                throw;
            }
        }

        /// <summary>
        /// Releases the advisory lock.
        /// </summary>
        public async Task ReleaseAsync(string lockName, CancellationToken cancellationToken = default)
        {
            // A name this provider does not hold is not released, and the held lock is kept (SH-M006).
            if (!IsLocked || _lockConnection == null || _heldName != lockName)
            {
                return;
            }

            await UnlockAsync(cancellationToken).ConfigureAwait(false);
        }

        // Issues the unlock for the held name and closes the lock connection.
        private async Task UnlockAsync(CancellationToken cancellationToken)
        {
            if (!IsLocked || _lockConnection == null || _heldName == null)
            {
                return;
            }
            var lockName = _heldName;

            try
            {
                using var cmd = _lockConnection.CreateCommand();
                switch (Dialect)
                {
                    case SqlDialect.PostgreSql:
                        cmd.CommandText = $"SELECT pg_advisory_unlock({GetLockKey(lockName)})";
                        break;
                    case SqlDialect.MSSql:
                        cmd.CommandText = "EXEC sp_releaseapplock @Resource = @res, @LockOwner = 'Session'";
                        AddParam(cmd, "@res", lockName);
                        break;
                    case SqlDialect.MySql:
                        cmd.CommandText = "SELECT RELEASE_LOCK(@res)";
                        AddParam(cmd, "@res", lockName);
                        break;
                    default:
                        cmd.CommandText = string.Empty;
                        break;
                }

                if (!string.IsNullOrEmpty(cmd.CommandText))
                {
                    await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
                }
            }
            catch (DbException)
            {
                // The connection is closed below. Closing returns it to the pool, which does not end the session, so this
                // is only a fallback: the unlock above is what frees the lock (SH-M019).
            }

            IsLocked = false;
            _heldName = null;
            await ReleaseConnectionAsync().ConfigureAwait(false);
        }

        private enum SqlDialect { PostgreSql, MSSql, MySql, Other }

        /// <summary>
        /// Which advisory-lock dialect to use, derived from the connector type name.
        /// </summary>
        private static SqlDialect Dialect
        {
            get
            {
                var name = typeof(DB).Name;
                if (name.Contains("PostgreSQL", StringComparison.OrdinalIgnoreCase) || name.Contains("Postgres", StringComparison.OrdinalIgnoreCase))
                    return SqlDialect.PostgreSql;
                if (name.Contains("MSSql", StringComparison.OrdinalIgnoreCase) || name.Contains("SqlServer", StringComparison.OrdinalIgnoreCase))
                    return SqlDialect.MSSql;
                if (name.Contains("MySQL", StringComparison.OrdinalIgnoreCase))
                    return SqlDialect.MySql;
                return SqlDialect.Other;
            }
        }

        private static void AddParam(DbCommand cmd, string name, object value)
        {
            var p = cmd.CreateParameter();
            p.ParameterName = name;
            p.Value = value;
            cmd.Parameters.Add(p);
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

        // SH-M019: disposing unlocks before closing. Closing returns the connection to the pool, which keeps the session —
        // and a MySQL GET_LOCK or a session-owned sp_getapplock with it — until the pooled connection is reused or evicted,
        // so another pool (another process) could not take the lock (measured on MySQL).
        public async ValueTask DisposeAsync()
        {
            if (!_disposed)
            {
                _disposed = true;
                try
                {
                    await UnlockAsync(CancellationToken.None).ConfigureAwait(false);
                }
                finally
                {
                    IsLocked = false;
                    _heldName = null;
                    await ReleaseConnectionAsync().ConfigureAwait(false);
                }
            }
        }

        public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
}
