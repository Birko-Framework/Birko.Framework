using Birko.Data.Filters;
using Birko.Data.Models;
using Birko.Data.Tenant.Models;
using System;
using System.Linq;
using System.Linq.Expressions;

namespace Birko.Data.Tenant.Filters
{
    public class ModelByTenant<TModel> : IRepositoryFilter<TModel>
         where TModel : AbstractModel, ITenant
    {
        public Guid? TenantId { get; set; }
        public Expression<Func<TModel, bool>>? BaseFilter { get; set; }

        public ModelByTenant(Guid? tenantId, Expression<Func<TModel, bool>>? filter = null)
        {
            TenantId = tenantId;
            BaseFilter = filter;
        }

        public virtual Expression<Func<TModel, bool>>? Filter()
        {
            if (TenantId != null && TenantId != Guid.Empty)
            {
                Expression<Func<TModel, bool>> right = (x) => x.TenantId == TenantId;
                return (BaseFilter != null)
                      ? Expression.Lambda<Func<TModel, bool>>(Expression.AndAlso(BaseFilter.Body, right.Body), BaseFilter.Parameters.Concat(right.Parameters.Skip(1)).Distinct())
                      : right;
            }
            return BaseFilter;
        }
    }
}
