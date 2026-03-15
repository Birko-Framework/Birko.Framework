using System;
using System.Linq.Expressions;
using Birko.Data.Filters;

namespace Birko.Models.Users.Filters
{
    public class UserRole : IFilter<Models.Users.UserRole>
    {
        public Guid? UserGuid { get; set; }
        public Guid? RoleGuid { get; set; }
        public Guid? AgendaGuid { get; set; }

        /// <summary>
        /// When true, only return global role assignments (AgendaGuid is null).
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

            if (AgendaGuid.HasValue)
            {
                var guid = AgendaGuid.Value;
                result = Combine(result, x => x.AgendaGuid == guid);
            }

            if (GlobalOnly.HasValue && GlobalOnly.Value)
            {
                result = Combine(result, x => x.AgendaGuid == null);
            }

            return result;
        }

        private static Expression<Func<Models.Users.UserRole, bool>> Combine(
            Expression<Func<Models.Users.UserRole, bool>>? left,
            Expression<Func<Models.Users.UserRole, bool>> right)
        {
            if (left == null) return right;
            var param = left.Parameters[0];
            var body = Expression.AndAlso(
                left.Body,
                Expression.Invoke(right, param));
            return Expression.Lambda<Func<Models.Users.UserRole, bool>>(body, param);
        }
    }
}
