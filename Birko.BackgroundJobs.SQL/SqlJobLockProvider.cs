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
                cmd.CommandTimeout = (int)timeout.TotalSeconds + 1;

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
                        AddParam(cmd, "@to", (int)timeout.TotalMilliseconds);
                        var ms = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
                        IsLocked = ms != null && ms != DBNull.Value && Convert.ToInt32(ms) >= 0;
                        break;

                    case SqlDialect.MySql:
                        // GET_LOCK returns 1 on success, 0 on timeout, NULL on error.
                        cmd.CommandText = "SELECT GET_LOCK(@res, @to)";
                        AddParam(cmd, "@res", lockName);
                        AddParam(cmd, "@to", (int)timeout.TotalSeconds);
                        var my = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
                        IsLocked = my != null && my != DBNull.Value && Convert.ToInt64(my) == 1;
                        break;
                }

                if (!IsLocked)
                {
                    await ReleaseConnectionAsync().ConfigureAwait(false);
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
            if (!IsLocked || _lockConnection == null)
            {
                return;
            }

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
                // Lock released when connection closes
            }

            IsLocked = false;
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
