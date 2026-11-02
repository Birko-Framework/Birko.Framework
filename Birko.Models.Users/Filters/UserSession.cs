using System;
using System.Linq.Expressions;
using Birko.Data.Filters;
using static Birko.Models.Users.Filters.FilterExpressions;

namespace Birko.Models.Users.Filters
{
    public class UserSession : IFilter<Models.Users.UserSession>
    {
        public Guid? UserGuid { get; set; }
        public Guid? TenantGuid { get; set; }

        /// <summary>Only sessions not revoked and not expired at this instant (UTC).</summary>
        public DateTime? ActiveAt { get; set; }

        public Expression<Func<Models.Users.UserSession, bool>>? Filter()
        {
            Expression<Func<Models.Users.UserSession, bool>>? result = null;

            if (UserGuid.HasValue)
            {
                var guid = UserGuid.Value;
                result = Combine(result, x => x.UserGuid == guid);
            }

            if (TenantGuid.HasValue)
            {
                var guid = TenantGuid.Value;
                result = Combine(result, x => x.TenantGuid == guid);
            }

            if (ActiveAt.HasValue)
            {
                var at = ActiveAt.Value;
                result = Combine(result, x => x.RevokedAt == null && x.ExpiresAt > at);
            }

            return result;
        }
    }
}
