using Birko.Data.Filters;
using Birko.Data.Stores;
using Birko.Configuration;
using Birko.Serialization;
using Birko.Serialization.Json;
using System;
using System.Reflection;
using System.Collections.Generic;

namespace Birko.Data.Repositories
{
    /// <summary>
    /// Provides a base implementation for sync repositories with ViewModel/Model separation and change tracking.
    /// </summary>
    /// <typeparam name="TViewModel">The type of view model.</typeparam>
    /// <typeparam name="TModel">The type of data model.</typeparam>
    public abstract class AbstractViewModelRepository<TViewModel, TModel>
        : IViewModelRepository<TViewModel, TModel>
        where TModel : Models.AbstractModel
        where TViewModel : Models.ILoadable<TModel>
    {
        #region Properties and Fields

        private bool _isReadMode = false;
        protected IDictionary<Guid, byte[]> _modelHash = new Dictionary<Guid, byte[]>();
        protected ISerializer Serializer { get; set; }
        protected IStore<TModel>? Store { get; set; }

        /// <summary>
        /// Gets or sets read mode. When enabled, change tracking is disabled.
        /// </summary>
        public virtual bool ReadMode
        {
            get => _isReadMode;
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
        /// <param name="store">The store to use for data operations.</param>
        public AbstractViewModelRepository(IStore<TModel>? store, ISerializer? serializer = null)
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
            if (Store == null)
            {
                return Activator.CreateInstance<TModel>();
            }
            return Store.CreateInstance();
        }

        #endregion

        #region Change Tracking Methods

        protected virtual void StoreHash(TModel data)
        {
            if (!ReadMode && data != null && data.Guid.HasValue)
            {
                var hash = CalculateHash(data);
                _modelHash[data.Guid.Value] = hash;
            }
        }

        protected virtual byte[] CalculateHash(TModel data)
        {
            return Helpers.StringHelper.CalculateSHA256Hash(Serializer.Serialize(data));
        }

        protected virtual void RemoveHash(TModel data)
        {
            if (!ReadMode && data != null && data.Guid.HasValue)
            {
                _modelHash.Remove(data.Guid.Value);
            }
        }

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
        /// the update path (see <see cref="LoadModelInstanceForUpdate"/>), so an implementation must
        /// ASSIGN the fields it owns rather than accumulate into them -- appending to a collection on
        /// <paramref name="target"/> will now double it on update.
        /// </remarks>
        protected abstract void MapToModel(TViewModel source, TModel target);

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

        public virtual TModel LoadModelInstance(TViewModel model)
        {
            TModel result = CreateModelInstance();
            MapToModel(model, result);
            return result;
        }

        /// <summary>
        /// Builds the model an UPDATE should persist: the <b>stored</b> row with this ViewModel's
        /// fields mapped onto it.
        /// </summary>
        /// <remarks>
        /// SH-H034. <see cref="LoadModelInstance"/> is right for a CREATE and structurally wrong for an
        /// UPDATE: it returns a FRESH model carrying only what <see cref="MapToModel"/> assigns, and
        /// every backend writes an update whole -- <c>Connector.Update</c> renders every column of
        /// <c>table.GetSelectFields()</c>, and the portable stores replace the stored instance
        /// outright. A ViewModel is a PARTIAL projection by construction and cannot map the columns the
        /// framework owns (<c>CreatedAt</c>/<c>UpdatedAt</c> on <c>AbstractLogModel</c>, <c>TenantGuid</c>
        /// injected by <c>TenantStoreWrapper</c>), so an update built from a fresh instance blanks them
        /// silently. Reading first makes the write a merge.
        /// <para>
        /// Two consequences worth knowing. The read goes through <see cref="Store"/>, so the decorator
        /// chain (tenant, soft-delete, localization) applies -- deliberately, and the opposite of
        /// SH-H036's connector bypass. And <see cref="MapToModel"/> may now receive a POPULATED target,
        /// so it must ASSIGN its fields rather than accumulate into them; that is the natural reading of
        /// "maps ViewModel data onto a Model instance" and is now the stated contract.
        /// </para>
        /// <para>
        /// Cost, recorded rather than hidden: one extra read per updated entity, including on the bulk
        /// path, which turns an N-row bulk update into N reads plus the batch. Correctness over
        /// throughput -- a cheaper shape would have to know which columns <see cref="MapToModel"/>
        /// touched, and nothing does.
        /// </para>
        /// </remarks>
        protected virtual TModel LoadModelInstanceForUpdate(TViewModel model, out TModel? stored)
        {
            stored = null;
            TModel result = LoadModelInstance(model);
            if (Store == null || result?.Guid == null)
            {
                // No key, so there is no row to merge with, and the update degrades to the old
                // fresh-model write. That is the PRE-EXISTING no-op case rather than a new one: a model
                // carrying no Guid never matched a row on any backend (SQL renders `WHERE Guid = NULL`,
                // the portable stores guard on ContainsKey). It does mean the merge engages only when
                // MapToModel assigns the key -- which every repository that can update at all must do.
                return result!;
            }

            TModel? row = Store.Read(result.Guid.Value);
            if (row == null)
            {
                // Nothing to merge with, so this behaves exactly as it did before the merge existed.
                // NB no backend REPORTS a missing row -- SQL updates zero rows, the portable stores
                // silently do nothing -- so do not read this path as a failure surfacing anywhere.
                return result;
            }

            // `row` is the no-op baseline and is never written to; see Detach.
            stored = row;
            TModel target = Detach(row);
            MapToModel(model, target);
            return target;
        }

        /// <summary>
        /// Returns a faithful shallow copy, so the merge never writes to an object the store still owns.
        /// </summary>
        /// <remarks>
        /// <para>
        /// SH-H016's mechanism: <c>AbstractInMemoryStore</c>, <c>AbstractJsonStore</c> and
        /// <c>AbstractXmlStore</c> each return the instance held in their own collection, and a caching
        /// decorator returns the cached reference. Mapping the ViewModel straight onto that instance
        /// would apply the update to store state <b>before</b> and <b>independently of</b>
        /// <c>Store.Update</c> -- so a write that throws, or one the no-op check suppresses, would still
        /// have mutated the store, and on the file-backed stores the next unrelated write would flush
        /// that never-committed state to disk.
        /// </para>
        /// <para>
        /// <c>Object.MemberwiseClone</c> by reflection, for the reason <c>Birko.Data.Localization</c>'s
        /// twin records (TASK-313): a reflection copy over public writable properties silently drops
        /// anything without a public setter, and <c>AbstractModel.CopyTo</c> copies only <c>Guid</c>
        /// unless overridden. A memberwise clone copies every field, so it cannot lose data.
        /// </para>
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

        #region Core CRUD Operations

        /// <inheritdoc />
        public virtual long Count(IFilter<TModel>? filter = null)
        {
            if (Store == null)
            {
                return 0;
            }
            return Store.Count(filter?.Filter());
        }

        /// <summary>
        /// Reads a single entity matching the specified filter. Alias for Read.
        /// </summary>
        public virtual TViewModel? ReadOne(IFilter<TModel>? filter = null)
        {
            return Read(filter);
        }

        /// <summary>
        /// Reads the first entity matching the filter under an explicit ordering, **through the decorator
        /// chain** (SH-H036).
        /// </summary>
        /// <remarks>
        /// <para>This exists because the ordered read previously lived in an <b>extension</b> method
        /// (<c>Birko.Data.SQL.Extensions.IDataBaseRepositoryExtensions.ReadOne</c>) that reached
        /// <c>repository.Connector</c> — and <c>Connector</c> resolves through <c>GetUnwrappedStore</c>,
        /// which walks to the innermost store. Every decorator was therefore skipped, including
        /// <c>TenantStoreWrapper</c>, so the read returned the first matching row from <b>any</b> tenant.
        /// Soft-delete, localization and audit wrappers were dropped by the same call.</para>
        /// <para>An extension in another assembly cannot do better: <see cref="Store"/> is
        /// <c>protected</c>, so the decorated chain is unreachable from outside and <c>Connector</c> was
        /// the only handle available. The capability has to be an instance method to be implementable
        /// safely at all — which is why this replaces the extension rather than patching it.</para>
        /// <para>The two overloads differ only in arity, and C# prefers an applicable instance method over
        /// an extension — so before this existed, <c>ReadOne(filter)</c> was tenant-safe while
        /// <c>ReadOne(filter, orderBy)</c> silently was not. Adding an ordering to a working call changed
        /// its isolation. Keep both overloads on this class so that can never diverge again.</para>
        /// <para>Ordering needs the bulk read surface; when the configured store is not an
        /// <see cref="IBulkReadStore{T}"/> the ordering cannot be honoured and this degrades to the
        /// unordered <see cref="Read(IFilter{TModel})"/> — still decorator-correct, which is the property
        /// that matters here.</para>
        /// </remarks>
        public virtual TViewModel? ReadOne(IFilter<TModel>? filter, OrderBy<TModel>? orderBy)
        {
            if (Store == null)
            {
                return default;
            }

            if (orderBy == null || Store is not IBulkReadStore<TModel> bulk)
            {
                return Read(filter);
            }

            foreach (var model in bulk.Read(filter?.Filter(), orderBy, 1, 0))
            {
                return LoadInstance(model);
            }
            return default;
        }

        /// <inheritdoc />
        public virtual TViewModel? Read(IFilter<TModel>? filter = null)
        {
            if (Store == null)
            {
                return default;
            }
            var model = Store.Read(filter?.Filter());
            return LoadInstance(model);
        }

        /// <inheritdoc />
        public virtual Guid Create(TViewModel data, ProcessDataDelegate<TModel>? processDelegate = null)
        {
            if (ReadMode)
            {
                throw new InvalidOperationException("Repository is in Read Mode"); // CR-L239
            }
            if (Store == null || data == null)
            {
                return Guid.Empty;
            }

            TModel item = LoadModelInstance(data);
            // SH-H035: ProcessDataDelegate is a TRANSFORM, and no store reads a StoreDataDelegate's
            // return value (96 invocation sites, 0 consumers), so a delegate that returned a
            // REPLACEMENT instance used to be dropped and the pre-transform model persisted. Apply it
            // here, before the store sees the item -- the same fix the bulk path already carries
            // (CR-H110). StoreHash stays inside the store delegate because CreateCore assigns
            // data.Guid before invoking it, and the hash is keyed by Guid.
            // The store's CreateCore does `data.Guid ??= Guid.NewGuid()` *before* it invokes the store
            // delegate, so a ProcessDataDelegate used to be able to read the newly assigned key (to
            // stamp child rows, an audit record, an outbox message). Hoisting the transform out would
            // have silently taken that away, so the key is assigned here first; every store honours a
            // pre-assigned Guid, because every one of them uses `??=`.
            item.Guid ??= Guid.NewGuid();
            item = processDelegate?.Invoke(item) ?? item;
            var guid = Store.Create(item, (x) =>
            {
                StoreHash(x);
                return x;
            });
            data.LoadFrom(item);
            return guid;
        }

        /// <inheritdoc />
        public virtual void Update(TViewModel data, ProcessDataDelegate<TModel>? processDelegate = null)
        {
            if (ReadMode)
            {
                throw new InvalidOperationException("Repository is in Read Mode"); // CR-L239
            }
            if (Store == null || data == null)
            {
                return;
            }

            // SH-H034: map onto a detached copy of the STORED row, never onto a fresh instance.
            TModel item = LoadModelInstanceForUpdate(data, out _);
            // SH-H035: honour the transform's result (see Create).
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
            Store.Update(item);
            // The hash tracker's contract is unchanged: the stored hash is refreshed on a successful
            // update, as CheckHashChange(x) did inside the delegate. Nothing gates a write on it.
            StoreHash(item);
            data.LoadFrom(item);
        }

        /// <inheritdoc />
        public virtual void Delete(TViewModel data)
        {
            if (ReadMode)
            {
                throw new InvalidOperationException("Repository is in Read Mode"); // CR-L239
            }
            if (Store == null)
            {
                return;
            }
            TModel item = LoadModelInstance(data);
            Store.Delete(item);
        }

        #endregion

        #region Lifecycle Methods

        /// <inheritdoc />
        public virtual void Destroy()
        {
            Store?.Destroy();
        }

        #endregion
    }
}
