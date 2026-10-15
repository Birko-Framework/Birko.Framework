using System;
using System.Linq.Expressions;

namespace Birko.Data.Stores
{
    /// <summary>
    /// Abstract base class for synchronous data stores.
    /// Provides core CRUD and lifecycle operations for entities.
    /// Automatically initializes on first CRUD operation via lazy-init.
    /// </summary>
    /// <typeparam name="T">The type of entity, must inherit from <see cref="Models.AbstractModel"/>.</typeparam>
    public abstract class AbstractStore<T> : IStore<T>
         where T : Models.AbstractModel
    {
        private bool _initialized;
        private readonly object _initLock = new();
        // True for the call flow running THIS store's InitCore (TASK-163, SH-M316). A CRUD call made from
        // InitCore (seeding defaults through Create) re-enters EnsureInitialized on that flow; it must pass
        // straight through. Before, the re-entrant Monitor let it in, it saw _initialized == false and called
        // InitCore again — unbounded recursion to an uncatchable StackOverflowException. Per call flow and
        // per instance, like the connector's IsInitializing scope (TASK-270): another thread still waits.
        private readonly System.Threading.AsyncLocal<bool> _initializingHere = new();
        private readonly System.Threading.AsyncLocal<bool> _destroyingHere = new();

        #region Initialization and Lifecycle

        /// <summary>
        /// Ensures the store is initialized. Called automatically before CRUD operations.
        /// Uses double-checked locking for thread safety.
        /// </summary>
        protected void EnsureInitialized()
        {
            if (_initializingHere.Value) return;
            if (_initialized && CanTrustRememberedInitialization) return;
            lock (_initLock)
            {
                if (_initialized && CanTrustRememberedInitialization) return;
                _initializingHere.Value = true;
                try
                {
                    InitCore();
                }
                finally
                {
                    _initializingHere.Value = false;
                }
                _initialized = CanRememberInitialization;
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
        public void Init()
        {
            EnsureInitialized();
        }

        /// <summary>
        /// Core initialization logic. Override to set up storage backend (create tables, indexes, etc.).
        /// Called once automatically before the first CRUD operation, or explicitly via Init.
        /// </summary>
        protected abstract void InitCore();

        /// <summary>
        /// Removes the store's backing storage, then forgets the initialization, so the next operation
        /// provisions it again (TASK-163, SH-M307). Non-virtual on purpose (rule 12): when <c>Destroy</c> was
        /// abstract the latch reset had to be repeated in every store, and none did — a JSON store whose file
        /// was deleted, a Mongo store whose collection was dropped, kept writing as if they still existed.
        /// The latch is cleared even when <see cref="DestroyCore"/> throws: storage in an unknown state is
        /// re-provisioned, which costs one idempotent init, rather than trusted.
        /// </summary>
        public void Destroy()
        {
            // A DestroyCore override calling base.Destroy() (the pre-TASK-163 shape) lands back here and would
            // recurse until the process dies; refuse it with the fix in the message instead.
            if (_destroyingHere.Value)
                throw new InvalidOperationException(string.Format("{0}.{1} called {2}, which dispatches back to {1} — unbounded recursion. Call base.{1} to reach the parent store's destroy.", GetType().Name, nameof(DestroyCore), nameof(Destroy)));
            _destroyingHere.Value = true;
            try
            {
                DestroyCore();
            }
            finally
            {
                _destroyingHere.Value = false;
                _initialized = false;
            }
        }

        /// <summary>
        /// Core destroy logic: drop the table, delete the file, remove the collection. Override in concrete
        /// stores; <see cref="Destroy"/> resets the initialization afterwards.
        /// </summary>
        protected abstract void DestroyCore();

        #endregion

        #region Core CRUD Operations - Single Item

        /// <inheritdoc />
        public virtual Guid Create(T data, StoreDataDelegate<T>? storeDelegate = null)
        {
            EnsureInitialized();
            try
            {
                return CreateCore(data, storeDelegate);
            }
            catch (Exception ex) when (IsDuplicateKeyException(ex))
            {
                throw Birko.Data.Exceptions.StoreException.DuplicateCreate(typeof(T), ex);
            }
        }

        /// <summary>
        /// Whether <paramref name="ex"/> is the provider refusing a create because the key is already stored. A provider
        /// whose driver signals that with its own exception overrides this, and the public create methods turn exactly
        /// that case into <see cref="Birko.Data.Exceptions.StoreException.DuplicateCreate"/> (TASK-625).
        /// </summary>
        protected virtual bool IsDuplicateKeyException(Exception ex) => false;

        /// <summary>
        /// Core create implementation. Override in concrete stores.
        /// </summary>
        protected abstract Guid CreateCore(T data, StoreDataDelegate<T>? storeDelegate = null);

        /// <inheritdoc />
        public virtual T? Read(Expression<Func<T, bool>>? filter = null)
        {
            EnsureInitialized();
            return ReadCore(filter);
        }

        /// <summary>
        /// Core read implementation. Override in concrete stores.
        /// </summary>
        protected abstract T? ReadCore(Expression<Func<T, bool>>? filter = null);

        /// <inheritdoc />
        public virtual T? Read(Guid guid)
        {
            return Read((new Filters.ModelByGuid<T>(guid)).Filter());
        }

        /// <inheritdoc />
        public virtual void Update(T data, StoreDataDelegate<T>? storeDelegate = null)
        {
            EnsureInitialized();
            UpdateCore(data, storeDelegate);
        }

        /// <summary>
        /// Core update implementation. Override in concrete stores.
        /// </summary>
        protected abstract void UpdateCore(T data, StoreDataDelegate<T>? storeDelegate = null);

        /// <inheritdoc />
        public virtual void Delete(T data)
        {
            EnsureInitialized();
            DeleteCore(data);
        }

        /// <summary>
        /// Core delete implementation. Override in concrete stores.
        /// </summary>
        protected abstract void DeleteCore(T data);

        #endregion

        #region Query and Count Operations

        /// <inheritdoc />
        public virtual long Count(Expression<Func<T, bool>>? filter = null)
        {
            EnsureInitialized();
            return CountCore(filter);
        }

        /// <summary>
        /// Core count implementation. Override in concrete stores.
        /// </summary>
        protected abstract long CountCore(Expression<Func<T, bool>>? filter = null);

        #endregion

        #region Utility Methods

        /// <summary>
        /// Saves an entity, deciding between create and update based on whether it has a GUID.
        /// </summary>
        /// <param name="data">The entity to save.</param>
        /// <param name="storeDelegate">Optional delegate to process the entity before saving.</param>
        /// <remarks>An entity whose Guid is not stored is created under that Guid (TASK-555) — see <see cref="StoreSave"/>.</remarks>
        public virtual Guid Save(T data, StoreDataDelegate<T>? storeDelegate = null)
            => StoreSave.Save(data, storeDelegate, Create, Update);

        /// <summary>
        /// Creates a new instance of the entity type.
        /// </summary>
        /// <returns>A new instance of type T.</returns>
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
