using System;
using System.Linq.Expressions;
using Birko.Data.Filters;

namespace Birko.Models.Users.Filters
{
    public class UserLogin : IRepositoryFilter<Models.Users.UserLogin>
    {
        public Guid? UserGuid { get; set; }
        public string? Provider { get; set; }
        public string? ProviderKey { get; set; }
        public bool? IsVerified { get; set; }

        public Expression<Func<Models.Users.UserLogin, bool>>? Filter()
        {
            Expression<Func<Models.Users.UserLogin, bool>>? result = null;

            if (UserGuid.HasValue)
            {
                var guid = UserGuid.Value;
                result = Combine(result, x => x.UserGuid == guid);
            }

            if (!string.IsNullOrEmpty(Provider))
            {
                result = Combine(result, x => x.Provider == Provider);
            }

            if (!string.IsNullOrEmpty(ProviderKey))
            {
                result = Combine(result, x => x.ProviderKey == ProviderKey);
            }

            if (IsVerified.HasValue)
            {
                var verified = IsVerified.Value;
                result = Combine(result, x => x.IsVerified == verified);
            }

            return result;
        }

        private static Expression<Func<Models.Users.UserLogin, bool>> Combine(
            Expression<Func<Models.Users.UserLogin, bool>>? left,
            Expression<Func<Models.Users.UserLogin, bool>> right)
        {
            if (left == null) return right;
            var param = left.Parameters[0];
            var body = Expression.AndAlso(
                left.Body,
                Expression.Invoke(right, param));
            return Expression.Lambda<Func<Models.Users.UserLogin, bool>>(body, param);
        }
    }
}
