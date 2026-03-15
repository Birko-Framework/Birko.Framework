using System;
using System.Linq.Expressions;
using Birko.Data.Filters;

namespace Birko.Models.Users.Filters
{
    public class Role : IFilter<Models.Users.Role>
    {
        public string? Name { get; set; }
        public bool? IsSystem { get; set; }

        public Expression<Func<Models.Users.Role, bool>>? Filter()
        {
            Expression<Func<Models.Users.Role, bool>>? result = null;

            if (!string.IsNullOrEmpty(Name))
            {
                result = Combine(result, x => x.Name == Name);
            }

            if (IsSystem.HasValue)
            {
                var system = IsSystem.Value;
                result = Combine(result, x => x.IsSystem == system);
            }

            return result;
        }

        private static Expression<Func<Models.Users.Role, bool>> Combine(
            Expression<Func<Models.Users.Role, bool>>? left,
            Expression<Func<Models.Users.Role, bool>> right)
        {
            if (left == null) return right;
            var param = left.Parameters[0];
            var body = Expression.AndAlso(
                left.Body,
                Expression.Invoke(right, param));
            return Expression.Lambda<Func<Models.Users.Role, bool>>(body, param);
        }
    }
}
