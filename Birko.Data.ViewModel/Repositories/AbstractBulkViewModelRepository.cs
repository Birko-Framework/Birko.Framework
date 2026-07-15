using Birko.Data.Filters;
using Birko.Data.Stores;
using Birko.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;

namespace Birko.Data.Repositories
{
    /// <summary>
    /// Provides a base implementation for bulk repositories with ViewModel/Model separation.
    /// Extends <see cref="AbstractViewModelRepository{TViewModel, TModel}"/> with bulk operation capabilities.
    /// </summary>
    /// <typeparam name="TViewModel">The type of view model.</typeparam>
    /// <typeparam name="TModel">The type of data model.</typeparam>
    public abstract class AbstractBulkViewModelRepository<TViewModel, TModel>
        : AbstractViewModelRepository<TViewModel, TModel>
        , IBulkViewModelRepository<TViewModel, TModel>
        where TModel : Models.AbstractModel
        where TViewModel : Models.ILoadable<TModel>
    {
        #region Constructors and Initialization

        /// <summary>
        /// Initializes a new instance with dependency injection support.
        /// </summary>
        /// <param name="store">The bulk store to use for data operations.</param>
        public AbstractBulkViewModelRepository(Stores.IBulkStore<TModel>? store)
            : base(store)
        {
        }

        #endregion

        #region Core CRUD Operations - Bulk

        /// <inheritdoc />
        public virtual IEnumerable<TViewModel> Read(IFilter<TModel>? filter = null, int? limit = null, int? offset = null)
        {
            if (Store is not IBulkStore<TModel>)
            {
                throw new ArgumentException($"Store is not type of {typeof(IBulkStore<TModel>)}");
            }

            foreach (var item in ((IBulkStore<TModel>)Store).Read(filter?.Filter(), null, limit, offset))
            {
                var instance = LoadInstance(item);
                if (instance != null)
                {
                    yield return instance;
                }
            }
        }

        /// <inheritdoc />
        public virtual void Create(IEnumerable<TViewModel> data, ProcessDataDelegate<TModel>? processDelegate = null)
        {
            if (ReadMode) throw new InvalidOperationException("Repository is in Read Mode"); // CR-M180 / CR-L239
            if (Store is not IBulkStore<TModel>)
            {
                throw new ArgumentException($"Store is not type of {typeof(IBulkStore<TModel>)}");
            }

            (Store as IBulkStore<TModel>)?.Create(data.Select(x =>
            {
                TModel item = LoadModelInstance(x);
                // Capture the delegate's return value (ProcessDataDelegate is a transform); the bulk
                // path previously invoked it for side effects only and discarded a returned/wrapped
                // instance, unlike the single-item path (CR-H110).
                item = processDelegate?.Invoke(item) ?? item;
                return item;
            }));
        }

        /// <inheritdoc />
        public virtual void Update(IEnumerable<TViewModel> data, ProcessDataDelegate<TModel>? processDelegate = null)
        {
            if (ReadMode) throw new InvalidOperationException("Repository is in Read Mode"); // CR-M180 / CR-L239
            if (Store is not IBulkStore<TModel>)
            {
                throw new ArgumentException($"Store is not type of {typeof(IBulkStore<TModel>)}");
            }
            (Store as IBulkStore<TModel>)?.Update(data.Select(x =>
            {
                TModel item = LoadModelInstance(x);
                item = processDelegate?.Invoke(item) ?? item;  // honor the transform result (CR-H110)
                return item;
            }));
        }

        /// <inheritdoc />
        public virtual void Update(Expression<Func<TModel, bool>> filter, Action<TModel> updateAction)
        {
            if (ReadMode) throw new InvalidOperationException("Repository is in Read Mode"); // CR-M180 / CR-L239
            if (Store is not IBulkStore<TModel> bulkStore)
            {
                throw new ArgumentException($"Store is not type of {typeof(IBulkStore<TModel>)}");
            }
            bulkStore.Update(filter, updateAction);
        }

        /// <inheritdoc />
        public virtual void Update(Expression<Func<TModel, bool>> filter, PropertyUpdate<TModel> updates)
        {
            if (ReadMode) throw new InvalidOperationException("Repository is in Read Mode"); // CR-M180 / CR-L239
            if (Store is not IBulkStore<TModel> bulkStore)
            {
                throw new ArgumentException($"Store is not type of {typeof(IBulkStore<TModel>)}");
            }
            bulkStore.Update(filter, updates);
        }

        /// <inheritdoc />
        public virtual void Delete(Expression<Func<TModel, bool>> filter)
        {
            if (ReadMode) throw new InvalidOperationException("Repository is in Read Mode"); // CR-M180 / CR-L239
            if (Store is not IBulkStore<TModel> bulkStore)
            {
                throw new ArgumentException($"Store is not type of {typeof(IBulkStore<TModel>)}");
            }
            bulkStore.Delete(filter);
        }

        /// <inheritdoc />
        public virtual void Delete(IEnumerable<TViewModel> data)
        {
            if (ReadMode) throw new InvalidOperationException("Repository is in Read Mode"); // CR-M180 / CR-L239
            if (Store is not IBulkStore<TModel>)
            {
                throw new ArgumentException($"Store is not type of {typeof(IBulkStore<TModel>)}");
            }
            (Store as IBulkStore<TModel>)?.Delete(data.Select(LoadModelInstance));
        }

        #endregion
    }
}
