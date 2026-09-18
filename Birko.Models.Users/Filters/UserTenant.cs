using System;
using System.Linq.Expressions;
using Birko.Data.Filters;
using static Birko.Models.Users.Filters.FilterExpressions;

namespace Birko.Models.Users.Filters
{
    public class UserTenant : IFilter<Models.Users.UserTenant>
    {
        public Guid? UserGuid { get; set; }
        public Guid? TenantGuid { get; set; }
        public bool? IsOwner { get; set; }

        public Expression<Func<Models.Users.UserTenant, bool>>? Filter()
        {
            Expression<Func<Models.Users.UserTenant, bool>>? result = null;

            if (UserGuid.HasValue)
            {
                var guid = UserGuid.Value;
                result = Combine(result, x => x.UserGuid == guid);
            }

            if (TenantGuid.HasValue)
            {
                var guid = TenantGuid.Value;
                result = Combine(result, x => x.TenantGuid == guid);
            }

            if (IsOwner.HasValue)
            {
                var owner = IsOwner.Value;
                result = Combine(result, x => x.IsOwner == owner);
            }

            return result;
        }

    }
}
