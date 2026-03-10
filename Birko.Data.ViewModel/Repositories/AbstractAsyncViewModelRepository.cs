using Birko.Data.Filters;
using Birko.Data.Stores;
using System;
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
        where TModel : Models.AbstractModel, Models.ILoadable<TViewModel>
        where TViewModel : Models.ILoadable<TModel>
    {
        #region Properties and Fields

        private bool _isReadMode = false;
        protected IDictionary<Guid?, byte[]> _modelHash = new Dictionary<Guid?, byte[]>();
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
        public AbstractAsyncViewModelRepository(Stores.IAsyncStore<TModel>? store)
        {
            Store = store;
        }

        /// <inheritdoc />
        public virtual TViewModel CreateInstance()
        {
            return (TViewModel)Activator.CreateInstance(typeof(TViewModel), Array.Empty<object>());
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

        /// <summary>
        /// Stores a hash of the model data for change tracking.
        /// </summary>
        /// <param name="data">The model to hash.</param>
        protected virtual void StoreHash(TModel data)
        {
            if (!ReadMode && data != null && data.Guid != null)
            {
                var hash = CalculateHash(data);
                _modelHash[data.Guid] = hash;
            }
        }

        /// <summary>
        /// Calculates a hash of the model data.
        /// </summary>
        /// <param name="data">The model to hash.</param>
        /// <returns>The hash bytes.</returns>
        protected virtual byte[] CalculateHash(TModel data)
        {
            return Helpers.StringHelper.CalculateSHA256Hash(System.Text.Json.JsonSerializer.Serialize(data));
        }

        /// <summary>
        /// Removes the hash for a model.
        /// </summary>
        /// <param name="data">The model to remove the hash for.</param>
        protected virtual void RemoveHash(TModel data)
        {
            if (!ReadMode && data != null && data.Guid != null)
            {
                _modelHash.Remove(data.Guid);
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
            if (data != null && data.Guid != null)
            {
                var hash = CalculateHash(data);
                if (_modelHash.TryGetValue(data.Guid, out byte[]? storedHash)
                    && Helpers.ObjectHelper.CompareHash(storedHash, hash))
                {
                    result = false;
                }
            }

            if (update)
            {
                StoreHash(data);
            }

            return result;
        }

        #endregion

        #region Instance Loading Methods

        /// <summary>
        /// Loads a view model from a data model.
        /// </summary>
        /// <param name="model">The data model to load from.</param>
        /// <returns>The loaded view model, or default if model is null.</returns>
        public virtual TViewModel LoadInstance(TModel? model = null)
        {
            if (model == null)
            {
                return default(TViewModel);
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
            result.LoadFrom(model);
            return result;
        }

        #endregion

        #region Core CRUD Operations - Single Item

        /// <summary>
        /// Asynchronously reads a single entity by filter.
        /// </summary>
        public virtual async Task<TViewModel?> ReadAsync(IRepositoryFilter<TModel>? filter = null, CancellationToken ct = default)
        {
            if (Store == null)
            {
                return default;
            }
            var model = await Store.ReadAsync(filter?.Filter(), ct);
            return LoadInstance(model);
        }

        /// <inheritdoc />
        public virtual async Task<TViewModel?> ReadOneAsync(IRepositoryFilter<TModel>? filter = null, CancellationToken ct = default)
        {
            if (Store != null)
            {
                var model = await Store.ReadAsync(filter?.Filter(), ct);
                return LoadInstance(model);
            }
            return default;
        }

        /// <inheritdoc />
        public virtual async Task<Guid> CreateAsync(TViewModel data, ProcessDataDelegate<TModel>? processDelegate = null, CancellationToken ct = default)
        {
            if (ReadMode)
            {
                throw new AccessViolationException("Repository is in Read Mode");
            }
            if (Store == null || data == null)
            {
                return Guid.Empty;
            }

            TModel item = LoadModelInstance(data);
            var guid = await Store.CreateAsync(item, (x) =>
            {
                x = processDelegate?.Invoke(x) ?? x;
                StoreHash(x);
                return x;
            }, ct);
            data.LoadFrom(item);
            return guid;
        }

        /// <inheritdoc />
        public virtual async Task UpdateAsync(TViewModel data, ProcessDataDelegate<TModel>? processDelegate = null, CancellationToken ct = default)
        {
            if (ReadMode)
            {
                throw new AccessViolationException("Repository is in Read Mode");
            }
            if (Store == null || data == null)
            {
                return;
            }

            TModel item = LoadModelInstance(data);
            await Store.UpdateAsync(item, (x) =>
            {
                x = processDelegate?.Invoke(x) ?? x;
                if (CheckHashChange(x))
                {
                    return x;
                }
                return null;
            }, ct);
            data.LoadFrom(item);
        }

        /// <inheritdoc />
        public virtual async Task DeleteAsync(TViewModel data, CancellationToken ct = default)
        {
            if (ReadMode)
            {
                throw new AccessViolationException("Repository is in Read Mode");
            }
            if (Store == null)
            {
                return;
            }
            var item = (TModel)Activator.CreateInstance(data.GetType(), Array.Empty<object>());
            item.LoadFrom(data);
            await Store.DeleteAsync(item, ct);
        }

        #endregion

        #region Query and Count Operations

        /// <inheritdoc />
        public virtual async Task<long> CountAsync(IRepositoryFilter<TModel>? filter = null, CancellationToken ct = default)
        {
            if (Store == null)
            {
                return 0;
            }
            return await Store.CountAsync(filter?.Filter(), ct);
        }

        #endregion

        #region Lifecycle Methods

        /// <inheritdoc />
        public virtual async Task DestroyAsync(CancellationToken ct = default)
        {
            if (Store != null)
            {
                await Store.DestroyAsync(ct);
            }
        }

        #endregion
    }
}
