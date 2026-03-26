using System;
using System.Linq.Expressions;
using Birko.Data.Expressions;
using Birko.Data.Filters;

namespace Birko.Models.Users.Filters
{
    public class UserRole : IFilter<Models.Users.UserRole>
    {
        public Guid? UserGuid { get; set; }
        public Guid? RoleGuid { get; set; }
        public Guid? TenantGuid { get; set; }

        /// <summary>
        /// When true, only return global role assignments (TenantGuid is null).
        /// </summary>
        public bool? GlobalOnly { get; set; }

        public Expression<Func<Models.Users.UserRole, bool>>? Filter()
        {
            Expression<Func<Models.Users.UserRole, bool>>? result = null;

            if (UserGuid.HasValue)
            {
                var guid = UserGuid.Value;
                result = Combine(result, x => x.UserGuid == guid);
            }

            if (RoleGuid.HasValue)
            {
                var guid = RoleGuid.Value;
                result = Combine(result, x => x.RoleGuid == guid);
            }

            if (TenantGuid.HasValue)
            {
                var guid = TenantGuid.Value;
                result = Combine(result, x => x.TenantGuid == guid);
            }

            if (GlobalOnly.HasValue && GlobalOnly.Value)
            {
                result = Combine(result, x => x.TenantGuid == null);
            }

            return result;
        }

        private static Expression<Func<Models.Users.UserRole, bool>> Combine(
            Expression<Func<Models.Users.UserRole, bool>>? left,
            Expression<Func<Models.Users.UserRole, bool>> right)
        {
            return ExpressionParameterReplacer.AndAlso(left, right);
        }
    }
}
