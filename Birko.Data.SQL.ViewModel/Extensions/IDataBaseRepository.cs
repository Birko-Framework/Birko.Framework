using Birko.Data.Filters;
using Birko.Data.Repositories;
using Birko.Data.SQL.Repositories;
using System;
using System.Collections.Generic;
using System.Linq.Expressions;

namespace Birko.Data.SQL.Extensions
{
    public static class IDataBaseRepositoryExtensions
    {
        public static TViewModel? ReadOne<TRepository, TConnector, TViewModel, TModel>(this TRepository repository, IFilter<TModel>? filter = null, IDictionary<Expression<Func<TModel, object>>, bool>? orderByExpr = null)
            where TRepository : AbstractViewModelRepository<TViewModel, TModel>, IDataBaseRepository<TConnector, TViewModel, TModel>
            where TConnector : SQL.Connectors.AbstractConnector
            where TModel : Models.AbstractModel, Models.ILoadable<TViewModel>
            where TViewModel : Models.ILoadable<TModel>
        {
            if (repository.Connector != null)
            {
                foreach (TModel item in repository.Connector.Select<TModel, object>(typeof(TModel), filter?.Filter(), orderByExpr, 1, 0))
                {
                    return repository.LoadInstance(item);
                }
            }
            return default;
        }
    }
}
