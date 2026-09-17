using Birko.Data.Filters;
using Birko.Data.Stores;
using Birko.Configuration;
using Birko.Serialization;
using Birko.Serialization.Json;
using System;
using System.Reflection;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Birko.Data.Repositories
{
    /// <summary>
    /// Provides a base implementation for async repositories with ViewModel/Model separation and change tracking.
    /// </summary>
    /// <typeparam name="TViewModel">The type of view model.</typeparam>
    /// <typeparam name="TModel">The type of data model.</typeparam>
    public abstract class AbstractAsyncViewModelRepository<TViewModel, TModel>
        : IAsyncViewModelRepository<TViewModel, TModel>
        where TModel : Models.AbstractModel
        where TViewModel : Models.ILoadable<TModel>
    {
        #region Properties and Fields

        private bool _isReadMode = false;
        protected IDictionary<Guid, byte[]> _modelHash = new Dictionary<Guid, byte[]>();
        protected ISerializer Serializer { get; set; }
        protected IAsyncStore<TModel>? Store { get; set; }

        /// <inheritdoc />
        public virtual bool ReadMode
        {
            get
            {
                return _isReadMode;
            }
            set
            {
                _isReadMode = value;
                if (_isReadMode && _modelHash.Count > 0)
                {
                    _modelHash.Clear();
                }
            }
        }

        #endregion

        #region Constructors and Initialization

        /// <summary>
        /// Initializes a new instance with dependency injection support.
        /// </summary>
        /// <param name="store">The async store to use for data operations.</param>
        public AbstractAsyncViewModelRepository(Stores.IAsyncStore<TModel>? store, ISerializer? serializer = null)
        {
            Store = store;
            Serializer = serializer ?? new SystemJsonSerializer();
        }

        /// <inheritdoc />
        public virtual TViewModel CreateInstance()
        {
            return (TViewModel)Activator.CreateInstance(typeof(TViewModel), Array.Empty<object>())!;
        }

        /// <inheritdoc />
        public virtual TModel CreateModelInstance()
        {
            if (Store == null) return Activator.CreateInstance<TModel>();
            return Store.CreateInstance();
        }

        #endregion

        #region Change Tracking Methods

        /// <summary>
        /// Stores a hash of the model data for change tracking.
        /// </summary>
        /// <param name="data">The model to hash.</param>
        protected virtual void StoreHash(TModel data)
        {
            if (!ReadMode && data != null && data.Guid.HasValue)
            {
                var hash = CalculateHash(data);
                _modelHash[data.Guid.Value] = hash;
            }
        }

        /// <summary>
        /// Calculates a hash of the model data.
        /// </summary>
        /// <param name="data">The model to hash.</param>
        /// <returns>The hash bytes.</returns>
        protected virtual byte[] CalculateHash(TModel data)
        {
            return Helpers.StringHelper.CalculateSHA256Hash(Serializer.Serialize(data));
        }

        /// <summary>
        /// Removes the hash for a model.
        /// </summary>
        /// <param name="data">The model to remove the hash for.</param>
        protected virtual void RemoveHash(TModel data)
        {
            if (!ReadMode && data != null && data.Guid.HasValue)
            {
                _modelHash.Remove(data.Guid.Value);
            }
        }

        /// <summary>
        /// Checks if the model data has changed by comparing hashes.
        /// </summary>
        /// <param name="data">The model to check.</param>
        /// <param name="update">Whether to update the stored hash.</param>
        /// <returns>True if the data has changed, false otherwise.</returns>
        protected virtual bool CheckHashChange(TModel data, bool update = true)
        {
            var result = true;
            if (data != null && data.Guid.HasValue)
            {
                var hash = CalculateHash(data);
                if (_modelHash.TryGetValue(data.Guid.Value, out byte[]? storedHash)
                    && Helpers.ObjectHelper.CompareHash(storedHash, hash))
                {
                    result = false;
                }
            }

            if (update && data != null)
            {
                StoreHash(data);
            }

            return result;
        }

        #endregion

        #region Instance Loading Methods

        /// <summary>
        /// Maps ViewModel data onto a Model instance.
        /// Override in concrete repositories to define the ViewModel→Model mapping.
        /// </summary>
        /// <remarks>
        /// SH-H034: <paramref name="target"/> is a FRESH model on the create path and the STORED row on
        /// the update path (see <see cref="LoadModelInstanceForUpdateAsync"/>), so an implementation must
        /// ASSIGN the fields it owns rather than accumulate into them -- appending to a collection on
        /// <paramref name="target"/> will now double it on update.
        /// </remarks>
        protected abstract void MapToModel(TViewModel source, TModel target);

        /// <summary>
        /// Loads a view model from a data model.
        /// </summary>
        /// <param name="model">The data model to load from.</param>
        /// <returns>The loaded view model, or default if model is null.</returns>
        public virtual TViewModel? LoadInstance(TModel? model = null)
        {
            if (model == null)
            {
                return default;
            }
            TViewModel result = CreateInstance();
            result.LoadFrom(model);
            StoreHash(model);
            return result;
        }

        /// <summary>
        /// Loads a data model from a view model.
        /// </summary>
        /// <param name="model">The view model to load from.</param>
        /// <returns>The loaded data model.</returns>
        public virtual TModel LoadModelInstance(TViewModel model)
        {
            TModel result = CreateModelInstance();
            MapToModel(model, result);
            return result;
        }

        /// <summary>
        /// Builds the model an UPDATE should persist: the <b>stored</b> row with this ViewModel's
        /// fields mapped onto it. Asynchronous twin of the synchronous repository's
        /// <c>LoadModelInstanceForUpdate</c>; see that method for why an update must read first.
        /// </summary>
        /// <remarks>
        /// SH-H034. <see cref="LoadModelInstance"/> is right for a CREATE and structurally wrong for an
        /// UPDATE: it returns a FRESH model carrying only what <see cref="MapToModel"/> assigns, and
        /// every backend writes an update whole. A ViewModel is a PARTIAL projection by construction and
        /// cannot map the columns the framework owns (<c>CreatedAt</c>/<c>UpdatedAt</c>,
        /// <c>TenantGuid</c>), so an update built from a fresh instance blanks them silently.
        /// <para>
        /// The read goes through <see cref="Store"/>, so the decorator chain applies. Cost, recorded
        /// rather than hidden: one extra read per updated entity, including on the bulk path.
        /// </para>
        /// </remarks>
        protected virtual async Task<MergedModel> LoadModelInstanceForUpdateAsync(TViewModel model, CancellationToken ct = default)
        {
            TModel result = LoadModelInstance(model);
            if (Store == null || result?.Guid == null)
            {
                // No key, so no row to merge with -- the PRE-EXISTING no-op case (a model with no Guid
                // never matched a row on any backend). The merge therefore engages only when MapToModel
                // assigns the key, which every repository that can update at all must do.
                return new MergedModel(result!, null);
            }

            TModel? row = await Store.ReadAsync(result.Guid.Value, ct).ConfigureAwait(false);
            if (row == null)
            {
                // Nothing to merge ONTO, so this behaves exactly as it did before the merge existed.
                // NB no backend REPORTS a missing row, and null does not mean "absent" -- the soft-delete
                // and tenant wrappers answer null for a row that exists and is hidden. See the
                // synchronous twin; distinguishing the two is [[TASK-454]].
                return new MergedModel(result, null);
            }

            // `row` is the no-op baseline and is never written to; see Detach.
            TModel target = Detach(row);
            MapToModel(model, target);
            return new MergedModel(target, row);
        }

        /// <summary>
        /// The model an update should persist, paired with the stored row it was merged onto.
        /// </summary>
        /// <remarks>
        /// <see cref="Stored"/> is the row exactly as read and is never written to; it is the baseline
        /// the no-op check compares against. It is <c>null</c> when there was no row to merge with.
        /// </remarks>
        protected readonly struct MergedModel
        {
            public MergedModel(TModel item, TModel? stored)
            {
                Item = item;
                Stored = stored;
            }

            /// <summary>The model to persist.</summary>
            public TModel Item { get; }

            /// <summary>The stored row as read, or <c>null</c> when none existed.</summary>
            public TModel? Stored { get; }
        }

        /// <summary>
        /// Returns a faithful shallow copy, so the merge never writes to an object the store still owns.
        /// </summary>
        /// <remarks>
        /// SH-H016's mechanism: the portable stores return the instance held in their own collection and
        /// a caching decorator returns the cached reference, so mapping onto it would apply the update to
        /// store state <b>before</b> and <b>independently of</b> <c>Store.UpdateAsync</c>.
        /// <c>Object.MemberwiseClone</c> by reflection, for the reason <c>Birko.Data.Localization</c>'s
        /// twin records (TASK-313): a property-wise copy silently drops anything without a public setter.
        /// </remarks>
        protected static TModel Detach(TModel entity)
        {
            // Defensive, not witnessed: Object.MemberwiseClone is guaranteed by the BCL.
            if (MemberwiseCloneMethod == null)
            {
                return entity;
            }
            return (TModel)MemberwiseCloneMethod.Invoke(entity, null)!;
        }

        private static readonly MethodInfo? MemberwiseCloneMethod =
            typeof(object).GetMethod("MemberwiseClone", BindingFlags.NonPublic | BindingFlags.Instance);

        #endregion

        #region Core CRUD Operations - Single Item

        /// <summary>
        /// Asynchronously reads a single entity by filter.
        /// </summary>
        public virtual async Task<TViewModel?> ReadAsync(IFilter<TModel>? filter = null, CancellationToken ct = default)
        {
            if (Store == null) return default;
            var model = await Store.ReadAsync(filter?.Filter(), ct);
            return LoadInstance(model);
        }

        /// <inheritdoc />
        public virtual async Task<TViewModel?> ReadOneAsync(IFilter<TModel>? filter = null, CancellationToken ct = default)
        {
            if (Store == null) return default;
            var model = await Store.ReadAsync(filter?.Filter(), ct);
            return LoadInstance(model);
        }

        /// <inheritdoc />
        public virtual async Task<Guid> CreateAsync(TViewModel data, ProcessDataDelegate<TModel>? processDelegate = null, CancellationToken ct = default)
        {
            if (ReadMode) throw new InvalidOperationException("Repository is in Read Mode"); // CR-L239
            if (Store == null || data == null) return Guid.Empty;

            TModel item = LoadModelInstance(data);
            // SH-H035: ProcessDataDelegate is a TRANSFORM, and no store reads a StoreDataDelegate's
            // return value (96 invocation sites, 0 consumers), so a delegate that returned a
            // REPLACEMENT instance used to be dropped and the pre-transform model persisted. Apply it
            // here, before the store sees the item -- the same fix the bulk path already carries
            // (CR-H110). StoreHash stays inside the store delegate because CreateCore assigns
            // data.Guid before invoking it, and the hash is keyed by Guid.
            // The store's CreateCoreAsync does `data.Guid ??= Guid.NewGuid()` *before* it invokes the
            // store delegate, so a ProcessDataDelegate used to be able to read the newly assigned key.
            // Hoisting the transform out would have silently taken that away, so the key is assigned
            // here first; every store honours a pre-assigned Guid, because every one uses `??=`.
            item.Guid ??= Guid.NewGuid();
            item = processDelegate?.Invoke(item) ?? item;
            var guid = await Store.CreateAsync(item, (x) =>
            {
                StoreHash(x);
                return x;
            }, ct);
            data.LoadFrom(item);
            return guid;
        }

        /// <inheritdoc />
        public virtual async Task UpdateAsync(TViewModel data, ProcessDataDelegate<TModel>? processDelegate = null, CancellationToken ct = default)
        {
            if (ReadMode) throw new InvalidOperationException("Repository is in Read Mode"); // CR-L239
            if (Store == null || data == null) return;

            // SH-H034: map onto a detached copy of the STORED row, never onto a fresh instance.
            var merged = await LoadModelInstanceForUpdateAsync(data, ct).ConfigureAwait(false);
            TModel item = merged.Item;
            // SH-H035: honour the transform's result (see CreateAsync).
            item = processDelegate?.Invoke(item) ?? item;

            // SH-H035, the half this task deliberately does NOT enable. The `return null!` that used to
            // come back from the store delegate was intended as "skip this write"; no backend reads that
            // value, so it suppressed nothing and every Update reached the store. Making it real would
            // have been a behaviour change far wider than the finding: AuditStoreWrapper,
            // TimestampStoreWrapper and EventSourcingStoreWrapper all sit INSIDE Store.Update in
            // StoreWrapperBuilder's recommended chain, so a suppressed write silently drops the audit
            // stamp, the UpdatedAt bump and the domain event -- and VersionedStoreWrapper's optimistic
            // check would stop seeing the caller's intent. Whether an unchanged save should be skipped is
            // a design decision with those consequences attached, and it is [[TASK-453]]. The write
            // therefore stays unconditional, exactly as it has always behaved.
            await Store.UpdateAsync(item, null, ct).ConfigureAwait(false);
            // The hash tracker's contract is unchanged: the stored hash is refreshed on a successful
            // update, as CheckHashChange(x) did inside the delegate. Nothing gates a write on it.
            StoreHash(item);
            data.LoadFrom(item);
        }

        /// <inheritdoc />
        public virtual async Task DeleteAsync(TViewModel data, CancellationToken ct = default)
        {
            if (ReadMode) throw new InvalidOperationException("Repository is in Read Mode"); // CR-L239
            if (Store == null) return;
            TModel item = CreateModelInstance();
            MapToModel(data, item);
            await Store.DeleteAsync(item, ct);
        }

        #endregion

        #region Query and Count Operations

        /// <inheritdoc />
        public virtual async Task<long> CountAsync(IFilter<TModel>? filter = null, CancellationToken ct = default)
        {
            if (Store == null) return 0;
            return await Store.CountAsync(filter?.Filter(), ct);
        }

        #endregion

        #region Lifecycle Methods

        /// <inheritdoc />
        public virtual async Task DestroyAsync(CancellationToken ct = default)
        {
            if (Store == null) return;
            await Store.DestroyAsync(ct);
        }

        #endregion
    }
}
