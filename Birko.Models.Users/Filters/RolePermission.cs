using System;
using System.Linq.Expressions;
using Birko.Data.Filters;
using static Birko.Models.Users.Filters.FilterExpressions;

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

    }
}
