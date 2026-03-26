using Birko.Data.Expressions;
using Birko.Data.Filters;
using Birko.Data.Models;
using Birko.Data.Tenant.Models;
using System;
using System.Linq.Expressions;

namespace Birko.Data.Tenant.Filters
{
    public class ModelByTenant<TModel> : IFilter<TModel>
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
                Expression<Func<TModel, bool>> tenantFilter = (x) => x.TenantGuid == TenantGuid;
                return ExpressionParameterReplacer.AndAlso(BaseFilter, tenantFilter);
            }
            return BaseFilter;
        }
    }
}
