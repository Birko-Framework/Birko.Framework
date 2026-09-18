using System;
using System.Linq.Expressions;
using Birko.Data.Filters;
using static Birko.Models.Users.Filters.FilterExpressions;

namespace Birko.Models.Users.Filters
{
    public class User : IFilter<Models.Users.User>
    {
        public string? UserName { get; set; }
        public string? Email { get; set; }
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

    }
}
