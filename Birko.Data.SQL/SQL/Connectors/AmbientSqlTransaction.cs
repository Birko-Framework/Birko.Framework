using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace Birko.Data.SQL.Connectors
{
    /// <summary>
    /// The transaction boundary an operation is currently running inside, if any.
    /// </summary>
    /// <remarks>
    /// Travels with the async control flow rather than living on the connector or the store.
    /// <para>
    /// Connectors are cached process-wide per (connector type, settings id) in
    /// <see cref="DataBase.GetConnector{T}"/>, so <see cref="AbstractConnector.SetExternalTransaction"/>
    /// mutates state shared by every caller against that database. Copying that pattern to the async
    /// path would make one request's transaction silently capture every concurrent request's writes.
    /// An <see cref="AsyncLocal{T}"/> scope has no such failure mode: it is visible only to the
    /// continuation chain that entered it, so it is correct under concurrent request threads even when
    /// the store is a singleton over a shared connector.
    /// </para>
    /// <para>
    /// Entries are keyed by <b>settings id</b>. A scope opened against database A therefore cannot
    /// capture a write to database B, and scopes against different databases nest and compose.
    /// </para>
    /// <para>
    /// <b>Why a mutable cell rather than a bare <see cref="AsyncLocal{T}"/> of the entry.</b> An
    /// <c>async</c> method cannot publish an <see cref="AsyncLocal{T}"/> value to its caller:
    /// <c>AsyncMethodBuilder.Start</c> saves the ambient <c>ExecutionContext</c> and restores it when the
    /// state machine returns or suspends, so a write made anywhere inside the method — including before
    /// its first <c>await</c> — is reverted on the way out. A boundary opened by an awaited
    /// <c>BeginAsync()</c> would therefore be invisible to the code that called it, which is exactly the
    /// shape of the defect being fixed. The cell is installed <b>synchronously</b> (see
    /// <see cref="InstallCell"/>) so the reference reaches the caller, and the boundary is published by
    /// mutating the cell rather than the <see cref="AsyncLocal{T}"/>.
    /// </para>
    /// </remarks>
    public static class AmbientSqlTransaction
    {
        /// <summary>
        /// Work to run once a transaction has committed (TASK-669), keyed by the transaction itself.
        /// </summary>
        /// <remarks>
        /// Keyed by the <see cref="DbTransaction"/>, not by an ambient <see cref="Entry"/>: a store given an explicit
        /// context publishes a fresh entry per operation (<c>EnterTransactionScope</c>), and a participant of a joined
        /// <c>SqlUnitOfWork</c> may run in another async flow, so the one thing every participant and the owner share is
        /// the transaction object. A <see cref="ConditionalWeakTable{TKey,TValue}"/> holds the registrations only as long
        /// as the transaction lives, so one nobody runs (a caller committing the <see cref="DbTransaction"/> itself) is
        /// collected with it. This is not per-caller state on a shared object (rule 36): the key IS the boundary.
        /// </remarks>
        private static readonly ConditionalWeakTable<DbTransaction, AfterCommitList> _afterCommit = new();

        private sealed class AfterCommitList
        {
            internal readonly object Gate = new();
            internal readonly List<(string Key, Func<Task> Callback)> Items = new();
        }

        /// <summary>
        /// Registers <paramref name="callback"/> to run once <paramref name="transaction"/> has committed. A second
        /// registration with the same <paramref name="key"/> on the same transaction is ignored, so a store writing many
        /// rows in one boundary registers its work once.
        /// </summary>
        /// <remarks>
        /// Run by the boundary's owner after a successful commit (<c>SqlUnitOfWork.CommitAsync</c>, the bulk stores'
        /// <c>Atomically(Async)</c>) and discarded on rollback or a failed commit. ⚠ A caller that commits the
        /// <see cref="DbTransaction"/> itself, outside those owners, runs nothing: the callbacks are dropped with the
        /// transaction. Register only work that is also safe to have done before the commit (e.g. invalidating a cache).
        /// </remarks>
        public static void OnCommitted(DbTransaction transaction, string key, Func<Task> callback)
        {
            if (transaction == null) throw new ArgumentNullException(nameof(transaction));
            if (key == null) throw new ArgumentNullException(nameof(key));
            if (callback == null) throw new ArgumentNullException(nameof(callback));
            var list = _afterCommit.GetValue(transaction, _ => new AfterCommitList());
            lock (list.Gate)
            {
                foreach (var item in list.Items)
                {
                    if (string.Equals(item.Key, key, StringComparison.Ordinal)) return;
                }
                list.Items.Add((key, callback));
            }
        }

        /// <summary>
        /// Runs and removes the callbacks registered on <paramref name="transaction"/>. Called by a boundary's owner
        /// after its commit succeeded. Each callback runs once and on its own: one that throws does not stop the rest,
        /// and its exception goes to <paramref name="recordFailure"/>, never to the caller, because the transaction has
        /// committed and reporting a failure would claim otherwise (rule 22's mirror). Swallowed means recorded
        /// (rule 25): owners pass the connector's <c>SubscriberFailures</c> recorder, channel <c>AfterCommit</c>.
        /// </summary>
        public static async Task RunCommittedAsync(DbTransaction transaction, Action<Exception>? recordFailure)
        {
            foreach (var callback in Take(transaction))
            {
                try
                {
                    await callback().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    recordFailure?.Invoke(ex);
                }
            }
        }

        /// <summary>
        /// The synchronous owner's form of <see cref="RunCommittedAsync"/> (<c>DataBaseBulkStore.Atomically</c>): each
        /// callback is waited on in turn.
        /// </summary>
        public static void RunCommitted(DbTransaction transaction, Action<Exception>? recordFailure)
        {
            foreach (var callback in Take(transaction))
            {
                try
                {
                    callback().GetAwaiter().GetResult();
                }
                catch (Exception ex)
                {
                    recordFailure?.Invoke(ex);
                }
            }
        }

        /// <summary>Drops the callbacks registered on <paramref name="transaction"/>: it rolled back or failed to commit.</summary>
        public static void DiscardCommitted(DbTransaction transaction) => Take(transaction);

        private static List<Func<Task>> Take(DbTransaction transaction)
        {
            var taken = new List<Func<Task>>();
            if (transaction == null || !_afterCommit.TryGetValue(transaction, out var list)) return taken;
            lock (list.Gate)
            {
                foreach (var item in list.Items) taken.Add(item.Callback);
                list.Items.Clear();
            }
            _afterCommit.Remove(transaction);
            return taken;
        }

        /// <summary>
        /// One entry in the ambient chain: a boundary against one database.
        /// </summary>
        public sealed class Entry
        {
            private int _rollbackOnly;

            internal Entry(string settingsId, DbConnection connection, DbTransaction transaction, Entry? parent)
            {
                SettingsId = settingsId;
                Connection = connection;
                Transaction = transaction;
                Parent = parent;
            }

            /// <summary>Settings id of the database this boundary covers.</summary>
            public string SettingsId { get; }

            /// <summary>The connection the boundary owns. Never disposed by a participating command.</summary>
            public DbConnection Connection { get; }

            /// <summary>The open transaction. Committed and rolled back only by the owner.</summary>
            public DbTransaction Transaction { get; }

            internal Entry? Parent { get; }

            /// <summary>
            /// True once a participant has rolled back. The owner's commit must fail rather than
            /// silently discarding the participant's decision.
            /// </summary>
            public bool IsRollbackOnly => Volatile.Read(ref _rollbackOnly) != 0;

            /// <summary>
            /// Marks the boundary as unable to commit. Set by a nested participant that rolled back.
            /// </summary>
            public void MarkRollbackOnly() => Volatile.Write(ref _rollbackOnly, 1);

            private int _ended;

            /// <summary>
            /// True once the owner has left this boundary. An ended entry is skipped by
            /// <see cref="Find"/> and <see cref="Current"/> even if some cell still references it.
            /// </summary>
            /// <remarks>
            /// This is what makes correctness independent of cell restoration, and it is not a nicety:
            /// a unit of work disposed through <c>DisposeAsync</c> cannot restore the previous cell,
            /// because an async method's AsyncLocal writes never reach its caller. Without the flag, a
            /// flow left holding a stale cell would keep resolving a boundary whose connection had
            /// already been disposed — every later read failing with "the connection is not open".
            /// </remarks>
            public bool IsEnded => Volatile.Read(ref _ended) != 0;

            internal void End() => Volatile.Write(ref _ended, 1);
        }

        /// <summary>
        /// The mutable cell an async flow shares with its caller. Only the head moves; the reference is
        /// what the <see cref="AsyncLocal{T}"/> carries.
        /// </summary>
        private sealed class Cell
        {
            internal Entry? Head;
        }

        private static readonly AsyncLocal<Cell?> _cell = new();

        /// <summary>
        /// The innermost ambient boundary in the current flow, regardless of which database it covers.
        /// </summary>
        public static Entry? Current
        {
            get
            {
                for (var entry = _cell.Value?.Head; entry != null; entry = entry.Parent)
                {
                    if (!entry.IsEnded)
                    {
                        return entry;
                    }
                }
                return null;
            }
        }

        /// <summary>
        /// The innermost ambient boundary covering <paramref name="settingsId"/>, or null if this flow
        /// is not inside a boundary against that database.
        /// </summary>
        public static Entry? Find(string? settingsId)
        {
            if (string.IsNullOrEmpty(settingsId))
            {
                return null;
            }
            for (var entry = _cell.Value?.Head; entry != null; entry = entry.Parent)
            {
                if (!entry.IsEnded && string.Equals(entry.SettingsId, settingsId, StringComparison.Ordinal))
                {
                    return entry;
                }
            }
            return null;
        }

        /// <summary>
        /// Installs a fresh cell for the current flow, seeded with whatever boundary is already in scope.
        /// </summary>
        /// <remarks>
        /// <b>Must be called from synchronous code</b> — a constructor, not an <c>async</c> method — or
        /// the installation is reverted before the caller can see it.
        /// <para>
        /// The cell is deliberately <b>fresh</b> rather than inherited. Two concurrent requests forked
        /// from a common ancestor would otherwise share one cell, and a boundary pushed by either would
        /// be visible to the other — the same cross-capture the process-wide connector suffers from.
        /// Seeding the new cell with the inherited head keeps nesting and multi-database lookup working
        /// while making every push private to the flow that made it.
        /// </para>
        /// </remarks>
        public static IDisposable InstallCell()
        {
            var previous = _cell.Value;
            _cell.Value = new Cell { Head = previous?.Head };
            return new CellScope(previous);
        }

        /// <summary>
        /// Hides every boundary from the current flow for the lifetime of the returned handle, so work
        /// done inside it runs on its own connection as though no boundary were open.
        /// </summary>
        /// <remarks>
        /// <b>Not a general escape hatch.</b> The one sanctioned use is DDL on a provider whose DDL is not
        /// transactional — see <see cref="AbstractConnectorBase.SupportsTransactionalDdl"/> and
        /// <c>AbstractConnector.DoDdlCommand</c>. On MySQL a <c>CREATE TABLE</c> issued on the boundary's
        /// own connection implicitly commits it, so the only way for the boundary to survive lazy
        /// schema-ensure is for the DDL not to touch that connection (TASK-243). Anything else that
        /// suppresses a boundary is escaping the boundary, which is the defect TASK-240 and TASK-242 exist
        /// to remove.
        /// <para>
        /// Suppression is a <b>fresh cell with no head</b> rather than a marker on the entry, so it hides
        /// the whole chain — including boundaries against other databases — and restores exactly what was
        /// there on dispose. It does not end, commit or roll back anything: the owner still holds its
        /// connection and transaction throughout.
        /// </para>
        /// <para>
        /// Safe from an <c>async</c> method as long as the suppressed work is awaited <i>inside</i> the
        /// scope: an <see cref="AsyncLocal{T}"/> write flows to callees but never back to the caller, so
        /// the worst an async caller can suffer is the suppression ending early on return — never leaking
        /// out. See <see cref="InstallCell"/> for the same mechanic stated from the other direction.
        /// </para>
        /// </remarks>
        public static IDisposable Suppress()
        {
            var previous = _cell.Value;
            _cell.Value = new Cell();
            return new CellScope(previous);
        }

        /// <summary>
        /// Enters a boundary for the current flow. Dispose the returned handle to leave it.
        /// </summary>
        /// <remarks>
        /// Entering does not open, close or dispose anything — the caller owns the connection and the
        /// transaction, and must keep them alive for the lifetime of the scope.
        /// <para>
        /// Safe to call from an <c>async</c> method when only the continuation needs to see the boundary
        /// (the per-store door does exactly that). To publish a boundary to a <i>caller</i>, install a
        /// cell synchronously first — see <see cref="InstallCell"/>.
        /// </para>
        /// </remarks>
        public static IDisposable Enter(string settingsId, DbConnection connection, DbTransaction transaction)
        {
            if (string.IsNullOrEmpty(settingsId)) throw new ArgumentNullException(nameof(settingsId));
            if (connection == null) throw new ArgumentNullException(nameof(connection));
            if (transaction == null) throw new ArgumentNullException(nameof(transaction));

            var cell = _cell.Value;
            if (cell == null)
            {
                cell = new Cell();
                _cell.Value = cell;
            }
            var previousHead = cell.Head;
            cell.Head = new Entry(settingsId, connection, transaction, previousHead);
            return new EntryScope(cell, cell.Head, previousHead);
        }

        private sealed class EntryScope : IDisposable
        {
            private readonly Cell _cell;
            private readonly Entry _entered;
            private readonly Entry? _previousHead;
            private bool _disposed;

            internal EntryScope(Cell cell, Entry entered, Entry? previousHead)
            {
                _cell = cell;
                _entered = entered;
                _previousHead = previousHead;
            }

            public void Dispose()
            {
                if (_disposed)
                {
                    return;
                }
                _disposed = true;
                // Marked ended FIRST, and unconditionally: popping the head only helps the cell this scope
                // was created against, and a nested flow may be holding a different cell that still
                // references this entry.
                _entered.End();
                // Pop only if still the head: a caller that disposes out of order removes its own entry
                // rather than having an exception thrown at it from a finally block.
                if (ReferenceEquals(_cell.Head, _entered))
                {
                    _cell.Head = _previousHead;
                }
            }
        }

        private sealed class CellScope : IDisposable
        {
            private readonly Cell? _previous;
            private bool _disposed;

            internal CellScope(Cell? previous)
            {
                _previous = previous;
            }

            public void Dispose()
            {
                if (_disposed)
                {
                    return;
                }
                _disposed = true;
                _cell.Value = _previous;
            }
        }
    }
}
