using System;
using System.Linq.Expressions;
using Birko.Data.Filters;

namespace Birko.Models.Users.Filters
{
    public class UserAgenda : IFilter<Models.Users.UserAgenda>
    {
        public Guid? UserGuid { get; set; }
        public Guid? AgendaGuid { get; set; }
        public bool? IsOwner { get; set; }

        public Expression<Func<Models.Users.UserAgenda, bool>>? Filter()
        {
            Expression<Func<Models.Users.UserAgenda, bool>>? result = null;

            if (UserGuid.HasValue)
            {
                var guid = UserGuid.Value;
                result = Combine(result, x => x.UserGuid == guid);
            }

            if (AgendaGuid.HasValue)
            {
                var guid = AgendaGuid.Value;
                result = Combine(result, x => x.AgendaGuid == guid);
            }

            if (IsOwner.HasValue)
            {
                var owner = IsOwner.Value;
                result = Combine(result, x => x.IsOwner == owner);
            }

            return result;
        }

        private static Expression<Func<Models.Users.UserAgenda, bool>> Combine(
            Expression<Func<Models.Users.UserAgenda, bool>>? left,
            Expression<Func<Models.Users.UserAgenda, bool>> right)
        {
            if (left == null) return right;
            var param = left.Parameters[0];
            var body = Expression.AndAlso(
                left.Body,
                Expression.Invoke(right, param));
            return Expression.Lambda<Func<Models.Users.UserAgenda, bool>>(body, param);
        }
    }
}
