using System;
using System.Threading;

namespace Birko.Data.Stores
{
    /// <summary>
    /// A transaction session published for the current async flow, found by the database it covers (TASK-700). The
    /// document stores (MongoDB, RavenDB) use it the way the SQL stores use <c>AmbientSqlTransaction</c>: a store with no
    /// session of its own writes inside the session its flow publishes for its database, so a unit of work or a write
    /// boundary covers every store on that database without per-caller state on the (often shared) store objects
    /// (rule 36).
    /// </summary>
    /// <typeparam name="TSession">The provider's session type; each type has its own chain.</typeparam>
    /// <remarks>
    /// The same mechanics as <c>AmbientSqlTransaction</c>, which predates this and stays as it is:
    /// <list type="bullet">
    /// <item><description>The chain lives in a mutable cell an <see cref="AsyncLocal{T}"/> carries. A write to an
    /// <see cref="AsyncLocal{T}"/> made in an <c>async</c> method never flows back to its caller, so an owner that must
    /// publish to its caller (a unit of work begun in <c>BeginAsync</c>) installs a cell synchronously first
    /// (<see cref="InstallCell"/>) and enters into it later.</description></item>
    /// <item><description>A fresh cell is seeded with the inherited head but is private to the flow that installed it,
    /// so two concurrent flows forked from one ancestor never see each other's entries.</description></item>
    /// <item><description>Leaving an entry marks it ended; <see cref="Find"/> skips ended entries, so a flow that kept a
    /// reference to an old cell cannot pick up a session whose owner has closed it.</description></item>
    /// </list>
    /// </remarks>
    public static class AmbientSession<TSession> where TSession : class
    {
        internal sealed class Cell
        {
            internal Entry? Head;
        }

        internal sealed class Entry
        {
            internal Entry(string key, TSession session, Entry? parent)
            {
                Key = key;
                Session = session;
                Parent = parent;
            }

            internal string Key { get; }
            internal TSession Session { get; }
            internal Entry? Parent { get; }
            private int _ended;
            private int _rollbackOnly;
            internal bool IsEnded => Volatile.Read(ref _ended) != 0;
            internal void End() => Volatile.Write(ref _ended, 1);
            internal bool IsRollbackOnly => Volatile.Read(ref _rollbackOnly) != 0;
            internal void MarkRollbackOnly() => Volatile.Write(ref _rollbackOnly, 1);
        }

        private static readonly AsyncLocal<Cell?> _cell = new();

        /// <summary>The innermost open session the current flow published for <paramref name="key"/>, or null.</summary>
        public static TSession? Find(string? key)
        {
            if (string.IsNullOrEmpty(key))
            {
                return null;
            }
            for (var entry = _cell.Value?.Head; entry != null; entry = entry.Parent)
            {
                if (!entry.IsEnded && string.Equals(entry.Key, key, StringComparison.Ordinal))
                {
                    return entry.Session;
                }
            }
            return null;
        }

        /// <summary>
        /// Marks the innermost open session the current flow published for <paramref name="key"/> rollback-only, as a
        /// participant that failed marks a SQL boundary: its owner must then refuse to commit (it reads
        /// <see cref="Scope.IsRollbackOnly"/>). False when nothing is published for the key.
        /// </summary>
        public static bool MarkRollbackOnly(string? key)
        {
            if (string.IsNullOrEmpty(key))
            {
                return false;
            }
            for (var entry = _cell.Value?.Head; entry != null; entry = entry.Parent)
            {
                if (!entry.IsEnded && string.Equals(entry.Key, key, StringComparison.Ordinal))
                {
                    entry.MarkRollbackOnly();
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Installs a fresh cell for the current flow, seeded with what is already in scope. <b>Call it from synchronous
        /// code</b> (a constructor, or before the first <c>await</c>), or the installation is undone before the caller
        /// sees it. Dispose the handle to restore the previous cell.
        /// </summary>
        public static IDisposable InstallCell()
        {
            var previous = _cell.Value;
            _cell.Value = new Cell { Head = previous?.Head };
            return new CellScope(previous);
        }

        /// <summary>
        /// Publishes <paramref name="session"/> for <paramref name="key"/> in the current flow until the handle is disposed.
        /// Publishing opens, commits and disposes nothing: the caller owns the session.
        /// </summary>
        public static Scope Enter(string key, TSession session)
        {
            ArgumentException.ThrowIfNullOrEmpty(key);
            ArgumentNullException.ThrowIfNull(session);
            var cell = _cell.Value;
            if (cell == null)
            {
                cell = new Cell();
                _cell.Value = cell;
            }
            var previous = cell.Head;
            var entered = new Entry(key, session, previous);
            cell.Head = entered;
            return new Scope(cell, entered, previous);
        }

        /// <summary>One published session, until disposed.</summary>
        public sealed class Scope : IDisposable
        {
            private readonly Cell _cell;
            private readonly Entry _entered;
            private readonly Entry? _previous;
            private bool _disposed;

            internal Scope(Cell cell, Entry entered, Entry? previous)
            {
                _cell = cell;
                _entered = entered;
                _previous = previous;
            }

            /// <summary>True once a participant marked this session rollback-only (<see cref="MarkRollbackOnly"/>).</summary>
            public bool IsRollbackOnly => _entered.IsRollbackOnly;

            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                _entered.End();
                if (ReferenceEquals(_cell.Head, _entered))
                {
                    _cell.Head = _previous;
                }
            }
        }

        private sealed class CellScope : IDisposable
        {
            private readonly Cell? _previous;
            private bool _disposed;

            internal CellScope(Cell? previous) => _previous = previous;

            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                _cell.Value = _previous;
            }
        }
    }
}
