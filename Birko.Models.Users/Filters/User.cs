using System;
using System.Linq;
using System.Linq.Expressions;
using Birko.Data.Filters;

namespace Birko.Models.Users.Filters
{
    public class User : IRepositoryFilter<Models.Users.User>
    {
        public string? UserName { get; set; }
        public string? Email { get; set; }
        public string? Role { get; set; }
        public bool? IsActive { get; set; }
        public bool? EmailVerified { get; set; }

        public Expression<Func<Models.Users.User, bool>>? Filter()
        {
            Expression<Func<Models.Users.User, bool>>? result = null;

            if (!string.IsNullOrEmpty(UserName))
            {
                result = Combine(result, x => x.UserName == UserName);
            }

            if (!string.IsNullOrEmpty(Email))
            {
                result = Combine(result, x => x.Email == Email);
            }

            if (!string.IsNullOrEmpty(Role))
            {
                result = Combine(result, x => x.Roles != null && x.Roles.Contains(Role));
            }

            if (IsActive.HasValue)
            {
                var active = IsActive.Value;
                result = Combine(result, x => x.IsActive == active);
            }

            if (EmailVerified.HasValue)
            {
                var verified = EmailVerified.Value;
                result = Combine(result, x => x.EmailVerified == verified);
            }

            return result;
        }

        private static Expression<Func<Models.Users.User, bool>> Combine(
            Expression<Func<Models.Users.User, bool>>? left,
            Expression<Func<Models.Users.User, bool>> right)
        {
            if (left == null) return right;
            var param = left.Parameters[0];
            var body = Expression.AndAlso(
                left.Body,
                Expression.Invoke(right, param));
            return Expression.Lambda<Func<Models.Users.User, bool>>(body, param);
        }
    }
}
