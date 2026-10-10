using System;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;

namespace Birko.Data.Stores
{
    /// <summary>
    /// Provides a base implementation for async data stores.
    /// Subclasses must implement core CRUD operations for their specific storage backend.
    /// Automatically initializes on first CRUD operation via lazy-init.
    /// </summary>
    /// <typeparam name="T">The type of entity, must inherit from <see cref="Models.AbstractModel"/>.</typeparam>
    public abstract class AbstractAsyncStore<T> : IAsyncStore<T>
        where T : Models.AbstractModel
    {
        private bool _initialized;
        private readonly SemaphoreSlim _initLock = new(1, 1);
        // True for the call flow running THIS store's InitCoreAsync (TASK-163, SH-M316). A CRUD call awaited
        // from InitCoreAsync re-enters EnsureInitializedAsync on that flow; it must pass straight through.
        // Before, it waited on the SemaphoreSlim the outer call held — SemaphoreSlim has no owner affinity —
        // and the store hung for good, where the sync base recursed to a StackOverflowException instead.
        // Per call flow and per instance, like the connector's IsInitializing scope (TASK-270).
        private readonly AsyncLocal<bool> _initializingHere = new();
        private readonly AsyncLocal<bool> _destroyingHere = new();

        #region Constructors and Initialization

        /// <summary>
        /// Initializes a new instance of the AbstractAsyncStore class.
        /// </summary>
        public AbstractAsyncStore()
        {
        }

        /// <summary>
        /// Ensures the store is initialized. Called automatically before CRUD operations.
        /// Uses double-checked locking for thread safety.
        /// </summary>
        protected async Task EnsureInitializedAsync(CancellationToken ct = default)
        {
            // Observe cancellation on every operation — all public async CRUD methods funnel through here,
            // including already-initialized stores that would otherwise return below without checking the token.
            ct.ThrowIfCancellationRequested();
            if (_initializingHere.Value) return;
            if (_initialized && CanTrustRememberedInitialization) return;
            await _initLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (_initialized && CanTrustRememberedInitialization) return;
                // Set inside this async method, so the value flows into InitCoreAsync and everything it awaits
                // and is restored automatically when this method returns to its caller.
                _initializingHere.Value = true;
                await InitCoreAsync(ct).ConfigureAwait(false);
                _initializingHere.Value = false;
                _initialized = CanRememberInitialization;
            }
            finally
            {
                _initLock.Release();
            }
        }

        /// <summary>
        /// Whether the initialization that just completed may be <b>remembered</b> — i.e. whether it is
        /// durable, or could still be undone by something outside this store.
        /// </summary>
        /// <remarks>
        /// <para>
        /// TASK-244. Default <c>true</c>: for a backend with no notion of a caller-owned transaction, an
        /// initialization that returned has happened. The SQL stores override it, because there a store's
        /// schema-ensure can run <i>inside</i> a caller's transaction boundary — so a rollback removes the
        /// table while this flag would still say the store is initialised, and the store then skips
        /// schema-ensure forever and writes against a table that is not there.
        /// </para>
        /// <para>
        /// <b>Answering "no" costs one idempotent re-run, and that asymmetry is the whole design.</b> A
        /// false negative re-issues <c>CREATE TABLE IF NOT EXISTS</c>; a false positive leaves a store
        /// permanently broken for the life of the process. So this errs toward re-running.
        /// </para>
        /// </remarks>
        protected virtual bool CanRememberInitialization => true;

        /// <summary>
        /// Whether an initialization that <i>was</i> remembered can still be trusted, asked on every
        /// operation rather than once.
        /// </summary>
        /// <remarks>
        /// <para>
        /// TASK-288, and it is <see cref="CanRememberInitialization"/>'s other half.
        /// <c>CanRememberInitialization</c> asks "may I remember what just happened?" at init time;
        /// this asks "does what I remembered still hold?" at use time. Default <c>true</c> — a backend
        /// where nothing can remove a store's schema underneath it has nothing to re-check.
        /// </para>
        /// <para>
        /// <b>The measurement it exists for.</b> With a table dropped beneath an initialised SQL store,
        /// five consecutive writes threw and the table was <b>never</b> recreated — <c>sqlite_master</c>
        /// held 0 rows throughout — while only a new store instance recovered it (consumer Symbio's
        /// TASK-627, reproduced in <c>VanishedTableHealingTests</c>). The database was fine; the store's
        /// remembered flag was not, so it short-circuited here and never reached <c>InitCore</c> again.
        /// Worse, a read of the same table answered <b>0 rows and no error</b> (TASK-211/TASK-285), so the
        /// surface looked healthy while every write failed.
        /// </para>
        /// <para>
        /// ⚠ <b>A PULL, deliberately.</b> The obvious wiring is for the store to subscribe to something on
        /// the connector — but connectors are cached process-wide per (type, settings id) while a web app
        /// resolves a store per request, so a subscriber list on that object grows without bound and keeps
        /// dead stores alive. That is TASK-204's defect. The SQL stores answer this by comparing a counter
        /// they recorded at init, which costs one read per operation and cannot leak.
        /// </para>
        /// <para>
        /// The asymmetry <see cref="CanRememberInitialization"/> records applies unchanged: answering "no"
        /// costs one idempotent <c>CREATE TABLE IF NOT EXISTS</c>, answering it wrongly leaves a store
        /// broken for the life of the process. So this too errs toward re-running.
        /// </para>
        /// </remarks>
        protected virtual bool CanTrustRememberedInitialization => true;

        /// <inheritdoc />
        public Task InitAsync(CancellationToken ct = default)
        {
            return EnsureInitializedAsync(ct);
        }

        /// <summary>
        /// Core initialization logic. Override to set up storage backend (create tables, indexes, etc.).
        /// Called once automatically before the first CRUD operation, or explicitly via InitAsync.
        /// </summary>
        protected abstract Task InitCoreAsync(CancellationToken ct = default);

        /// <summary>
        /// Removes the store's backing storage, then forgets the initialization, so the next operation
        /// provisions it again (TASK-163, SH-M307). Non-virtual on purpose (rule 12) — see
        /// <see cref="AbstractStore{T}.Destroy"/>. The latch is cleared even when
        /// <see cref="DestroyCoreAsync"/> throws.
        /// </summary>
        public async Task DestroyAsync(CancellationToken ct = default)
        {
            // A DestroyCoreAsync override awaiting base.DestroyAsync() (the pre-TASK-163 shape) lands back here
            // and would recurse until the process dies; refuse it with the fix in the message instead.
            if (_destroyingHere.Value)
                throw new InvalidOperationException(string.Format("{0}.{1} called {2}, which dispatches back to {1} — unbounded recursion. Call base.{1} to reach the parent store's destroy.", GetType().Name, nameof(DestroyCoreAsync), nameof(DestroyAsync)));
            _destroyingHere.Value = true;
            try
            {
                await DestroyCoreAsync(ct).ConfigureAwait(false);
            }
            finally
            {
                _initialized = false;
            }
        }

        /// <summary>
        /// Core destroy logic. Override in concrete stores; <see cref="DestroyAsync"/> resets the
        /// initialization afterwards.
        /// </summary>
        protected abstract Task DestroyCoreAsync(CancellationToken ct = default);

        #endregion

        #region Core CRUD Operations - Single Item

        /// <inheritdoc />
        public virtual async Task<Guid> CreateAsync(T data, StoreDataDelegate<T>? processDelegate = null, CancellationToken ct = default)
        {
            await EnsureInitializedAsync(ct).ConfigureAwait(false);
            return await CreateCoreAsync(data, processDelegate, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Core create implementation. Override in concrete stores.
        /// </summary>
        protected abstract Task<Guid> CreateCoreAsync(T data, StoreDataDelegate<T>? processDelegate = null, CancellationToken ct = default);

        /// <inheritdoc />
        public virtual Task<T?> ReadAsync(Guid guid, CancellationToken ct = default)
        {
            // Use the shared ModelByGuid filter (mirroring the sync AbstractStore.Read(Guid)) so both
            // paths stay identical if the filter ever encodes extra matching logic (CR-L205).
            return ReadAsync((new Birko.Data.Filters.ModelByGuid<T>(guid)).Filter(), ct);
        }

        /// <inheritdoc />
        public virtual async Task<T?> ReadAsync(Expression<Func<T, bool>>? filter = null, CancellationToken ct = default)
        {
            await EnsureInitializedAsync(ct).ConfigureAwait(false);
            return await ReadCoreAsync(filter, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Core read implementation. Override in concrete stores.
        /// </summary>
        protected abstract Task<T?> ReadCoreAsync(Expression<Func<T, bool>>? filter = null, CancellationToken ct = default);

        /// <inheritdoc />
        public virtual async Task UpdateAsync(T data, StoreDataDelegate<T>? processDelegate = null, CancellationToken ct = default)
        {
            await EnsureInitializedAsync(ct).ConfigureAwait(false);
            await UpdateCoreAsync(data, processDelegate, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Core update implementation. Override in concrete stores.
        /// </summary>
        protected abstract Task UpdateCoreAsync(T data, StoreDataDelegate<T>? processDelegate = null, CancellationToken ct = default);

        /// <inheritdoc />
        public virtual async Task DeleteAsync(T data, CancellationToken ct = default)
        {
            await EnsureInitializedAsync(ct).ConfigureAwait(false);
            await DeleteCoreAsync(data, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Core delete implementation. Override in concrete stores.
        /// </summary>
        protected abstract Task DeleteCoreAsync(T data, CancellationToken ct = default);

        #endregion

        #region Query and Count Operations

        /// <inheritdoc />
        public virtual async Task<long> CountAsync(Expression<Func<T, bool>>? filter = null, CancellationToken ct = default)
        {
            await EnsureInitializedAsync(ct).ConfigureAwait(false);
            return await CountCoreAsync(filter, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Core count implementation. Override in concrete stores.
        /// </summary>
        protected abstract Task<long> CountCoreAsync(Expression<Func<T, bool>>? filter = null, CancellationToken ct = default);

        #endregion

        #region Utility Methods

        /// <inheritdoc />
        public virtual async Task<Guid> SaveAsync(T data, StoreDataDelegate<T>? processDelegate = null, CancellationToken ct = default)
        {
            if (data == null)
            {
                return Guid.Empty;
            }

            if (data.Guid == null || data.Guid == Guid.Empty)
            {
                return await CreateAsync(data, processDelegate, ct);
            }
            else
            {
                await UpdateAsync(data, processDelegate, ct);
                return data.Guid!.Value;
            }
        }

        /// <inheritdoc />
        public virtual T CreateInstance()
        {
            try
            {
                return Activator.CreateInstance<T>();
            }
            catch (MissingMethodException)
            {
                return (T)Activator.CreateInstance(typeof(T), Array.Empty<object>())!;
            }
        }

        #endregion
    }
}
