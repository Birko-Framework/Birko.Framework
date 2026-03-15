using System;
using System.Linq.Expressions;
using Birko.Data.Filters;

namespace Birko.Models.Users.Filters
{
    public class RolePermission : IFilter<Models.Users.RolePermission>
    {
        public Guid? RoleGuid { get; set; }
        public string? PermissionCode { get; set; }

        /// <summary>
        /// Filter by permission code prefix (e.g. "iot:" returns all IoT permissions).
        /// </summary>
        public string? PermissionCodePrefix { get; set; }

        public Expression<Func<Models.Users.RolePermission, bool>>? Filter()
        {
            Expression<Func<Models.Users.RolePermission, bool>>? result = null;

            if (RoleGuid.HasValue)
            {
                var guid = RoleGuid.Value;
                result = Combine(result, x => x.RoleGuid == guid);
            }

            if (!string.IsNullOrEmpty(PermissionCode))
            {
                result = Combine(result, x => x.PermissionCode == PermissionCode);
            }

            if (!string.IsNullOrEmpty(PermissionCodePrefix))
            {
                result = Combine(result, x => x.PermissionCode.StartsWith(PermissionCodePrefix));
            }

            return result;
        }

        private static Expression<Func<Models.Users.RolePermission, bool>> Combine(
            Expression<Func<Models.Users.RolePermission, bool>>? left,
            Expression<Func<Models.Users.RolePermission, bool>> right)
        {
            if (left == null) return right;
            var param = left.Parameters[0];
            var body = Expression.AndAlso(
                left.Body,
                Expression.Invoke(right, param));
            return Expression.Lambda<Func<Models.Users.RolePermission, bool>>(body, param);
        }
    }
}
