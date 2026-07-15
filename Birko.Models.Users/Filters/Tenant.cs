using System;
using System.Linq.Expressions;
using Birko.Data.Filters;
using static Birko.Models.Users.Filters.FilterExpressions;

namespace Birko.Models.Users.Filters
{
    public class Tenant : IFilter<Models.Users.Tenant>
    {
        public string? Name { get; set; }
        public bool? IsDefault { get; set; }
        public bool? IsActive { get; set; }

        public Expression<Func<Models.Users.Tenant, bool>>? Filter()
        {
            Expression<Func<Models.Users.Tenant, bool>>? result = null;

            if (!string.IsNullOrEmpty(Name))
            {
                result = Combine(result, x => x.Name == Name);
            }

            if (IsDefault.HasValue)
            {
                var def = IsDefault.Value;
                result = Combine(result, x => x.IsDefault == def);
            }

            if (IsActive.HasValue)
            {
                var active = IsActive.Value;
                result = Combine(result, x => x.IsActive == active);
            }

            return result;
        }

    }
}
