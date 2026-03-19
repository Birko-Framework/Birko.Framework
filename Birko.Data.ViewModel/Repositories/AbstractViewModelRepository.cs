using Birko.Data.Filters;
using Birko.Data.Stores;
using Birko.Configuration;
using Birko.Serialization;
using Birko.Serialization.Json;
using System;
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
        where TModel : Models.AbstractModel, Models.ILoadable<TViewModel>
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
            result.LoadFrom(model);
            return result;
        }

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
                throw new AccessViolationException("Repository is in Read Mode");
            }
            if (Store == null || data == null)
            {
                return Guid.Empty;
            }

            TModel item = LoadModelInstance(data);
            var guid = Store.Create(item, (x) =>
            {
                x = processDelegate?.Invoke(x) ?? x;
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
                throw new AccessViolationException("Repository is in Read Mode");
            }
            if (Store == null || data == null)
            {
                return;
            }

            TModel item = LoadModelInstance(data);
            Store.Update(item, (x) =>
            {
                x = processDelegate?.Invoke(x) ?? x;
                if (CheckHashChange(x))
                {
                    return x;
                }
                return null!;
            });
            data.LoadFrom(item);
        }

        /// <inheritdoc />
        public virtual void Delete(TViewModel data)
        {
            if (ReadMode)
            {
                throw new AccessViolationException("Repository is in Read Mode");
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
