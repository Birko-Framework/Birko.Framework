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

        /// <summary>Serializes a model to decide whether an update changes it (<see cref="IsUnchanged"/>).</summary>
        protected ISerializer Serializer { get; set; }
        protected IAsyncStore<TModel>? Store { get; set; }

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

        #region Change Detection

        /// <summary>
        /// True when the model an update would write equals the row the update path just read, so the write would
        /// change nothing and is skipped (TASK-453, the user's decision). The sync twin's remarks give the reasoning:
        /// the baseline is the row just read, never a value cached earlier, and a skipped save has no audit stamp,
        /// <c>UpdatedAt</c> bump, domain event or version check. No row is never "unchanged".
        /// </summary>
        protected virtual bool IsUnchanged(TModel item, TModel? stored)
            => stored != null && Serializer.Serialize(item) == Serializer.Serialize(stored);

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
                // Nothing to merge ONTO, so the fresh model is written and the STORE reports the missing row:
                // UpdateAsync throws EntityNotFoundException for a key a backend does not hold (TASK-555), and
                // SoftDeleteStoreWrapper does the same for a soft-deleted row, which its Read hides (TASK-159). So a
                // hidden row is refused, never resurrected (TASK-454); a tenant-hidden one is refused by
                // TenantStoreWrapper with TenantMismatchException. See the synchronous twin.
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

        /// <summary>
        /// SH-M219 (TASK-162), on the ViewModel repositories too. With no store every operation answered as if it had
        /// worked (Guid.Empty, a no-op write, a count of 0, a null read); now it refuses.
        /// </summary>
        protected InvalidOperationException NoStore()
            => new($"{GetType().Name} has no store; construct it with one.");

        #region Core CRUD Operations - Single Item

        /// <summary>
        /// Asynchronously reads a single entity by filter.
        /// </summary>
        public virtual async Task<TViewModel?> ReadAsync(IFilter<TModel>? filter = null, CancellationToken ct = default)
        {
            if (Store == null) throw NoStore();
            var model = await Store.ReadAsync(filter?.Filter(), ct);
            return LoadInstance(model);
        }

        /// <inheritdoc />
        public virtual async Task<TViewModel?> ReadOneAsync(IFilter<TModel>? filter = null, CancellationToken ct = default)
        {
            if (Store == null) throw NoStore();
            var model = await Store.ReadAsync(filter?.Filter(), ct);
            return LoadInstance(model);
        }

        /// <inheritdoc />
        public virtual async Task<Guid> CreateAsync(TViewModel data, ProcessDataDelegate<TModel>? processDelegate = null, CancellationToken ct = default)
        {
            if (Store == null) throw NoStore();
            if (data == null) return Guid.Empty;

            TModel item = LoadModelInstance(data);
            // SH-H035: ProcessDataDelegate is a TRANSFORM, and no store reads a StoreDataDelegate's
            // return value (96 invocation sites, 0 consumers), so a delegate that returned a
            // REPLACEMENT instance used to be dropped and the pre-transform model persisted. Apply it
            // here, before the store sees the item -- the same fix the bulk path already carries
            // (CR-H110).
            // The store's CreateCoreAsync assigns the identity (AbstractModel.EnsureGuid) *before* it invokes the store
            // delegate, so a ProcessDataDelegate used to be able to read the newly assigned key. Hoisting the transform
            // out would have silently taken that away, so the key is assigned here first; every store keeps a
            // pre-assigned Guid (TASK-556 made that true of the SQL and RavenDB bulk paths too).
            item.EnsureGuid();
            item = processDelegate?.Invoke(item) ?? item;
            var guid = await Store.CreateAsync(item, null, ct);
            data.LoadFrom(item);
            return guid;
        }

        /// <inheritdoc />
        public virtual async Task UpdateAsync(TViewModel data, ProcessDataDelegate<TModel>? processDelegate = null, CancellationToken ct = default)
        {
            if (Store == null) throw NoStore();
            if (data == null) return;

            // SH-H034: map onto a detached copy of the STORED row, never onto a fresh instance.
            var merged = await LoadModelInstanceForUpdateAsync(data, ct).ConfigureAwait(false);
            TModel item = merged.Item;
            // SH-H035: honour the transform's result (see CreateAsync).
            item = processDelegate?.Invoke(item) ?? item;

            // TASK-453 (the user's decision): a save that changes nothing does not reach the store, judged against the
            // row just read (see IsUnchanged).
            if (!IsUnchanged(item, merged.Stored))
            {
                await Store.UpdateAsync(item, null, ct).ConfigureAwait(false);
            }
            data.LoadFrom(item);
        }

        /// <inheritdoc />
        public virtual async Task DeleteAsync(TViewModel data, CancellationToken ct = default)
        {
            if (Store == null) throw NoStore();
            if (data == null) return; // SH-M216 (TASK-162)
            // SH-M220 (TASK-162): through the LoadModelInstance hook, as every other path is; inlining it skipped an override.
            TModel item = LoadModelInstance(data);
            await Store.DeleteAsync(item, ct);
        }

        #endregion

        #region Query and Count Operations

        /// <inheritdoc />
        public virtual async Task<long> CountAsync(IFilter<TModel>? filter = null, CancellationToken ct = default)
        {
            if (Store == null) throw NoStore();
            return await Store.CountAsync(filter?.Filter(), ct);
        }

        #endregion

        #region Lifecycle Methods

        /// <inheritdoc />
        public virtual async Task DestroyAsync(CancellationToken ct = default)
        {
            if (Store == null) return; // no store, nothing to destroy (TASK-162)
            await Store.DestroyAsync(ct);
        }

        #endregion
    }
}
