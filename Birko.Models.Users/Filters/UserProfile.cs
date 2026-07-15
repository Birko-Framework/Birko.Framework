using System;
using System.Linq.Expressions;
using Birko.Data.Filters;
using static Birko.Models.Users.Filters.FilterExpressions;

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

    }
}
