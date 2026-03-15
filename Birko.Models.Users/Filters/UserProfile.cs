using System;
using System.Linq.Expressions;
using Birko.Data.Filters;

namespace Birko.Models.Users.Filters
{
    public class UserProfile : IFilter<Models.Users.UserProfile>
    {
        public Guid? UserGuid { get; set; }
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string? Locale { get; set; }
        public string? TimeZone { get; set; }

        public Expression<Func<Models.Users.UserProfile, bool>>? Filter()
        {
            Expression<Func<Models.Users.UserProfile, bool>>? result = null;

            if (UserGuid.HasValue)
            {
                var guid = UserGuid.Value;
                result = Combine(result, x => x.UserGuid == guid);
            }

            if (!string.IsNullOrEmpty(FirstName))
            {
                result = Combine(result, x => x.FirstName != null && x.FirstName.Contains(FirstName));
            }

            if (!string.IsNullOrEmpty(LastName))
            {
                result = Combine(result, x => x.LastName != null && x.LastName.Contains(LastName));
            }

            if (!string.IsNullOrEmpty(Locale))
            {
                result = Combine(result, x => x.Locale == Locale);
            }

            if (!string.IsNullOrEmpty(TimeZone))
            {
                result = Combine(result, x => x.TimeZone == TimeZone);
            }

            return result;
        }

        private static Expression<Func<Models.Users.UserProfile, bool>> Combine(
            Expression<Func<Models.Users.UserProfile, bool>>? left,
            Expression<Func<Models.Users.UserProfile, bool>> right)
        {
            if (left == null) return right;
            var param = left.Parameters[0];
            var body = Expression.AndAlso(
                left.Body,
                Expression.Invoke(right, param));
            return Expression.Lambda<Func<Models.Users.UserProfile, bool>>(body, param);
        }
    }
}
