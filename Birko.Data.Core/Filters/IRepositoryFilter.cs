using System;
using System.Linq.Expressions;

namespace Birko.Data.Filters
{
    public interface IRepositoryFilter<TModel>
        where TModel : Models.AbstractModel
    {
        Expression<Func<TModel, bool>>? Filter();
    }
}
