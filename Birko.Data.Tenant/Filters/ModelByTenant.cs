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
        public Guid? TenantGuid { get; set; }
        public Expression<Func<TModel, bool>>? BaseFilter { get; set; }

        public ModelByTenant(Guid? tenantGuid, Expression<Func<TModel, bool>>? filter = null)
        {
            TenantGuid = tenantGuid;
            BaseFilter = filter;
        }

        public virtual Expression<Func<TModel, bool>>? Filter()
        {
            if (TenantGuid != null && TenantGuid != Guid.Empty)
            {
                Expression<Func<TModel, bool>> right = (x) => x.TenantGuid == TenantGuid;
                return (BaseFilter != null)
                      ? Expression.Lambda<Func<TModel, bool>>(Expression.AndAlso(BaseFilter.Body, right.Body), BaseFilter.Parameters.Concat(right.Parameters.Skip(1)).Distinct())
                      : right;
            }
            return BaseFilter;
        }
    }
}
