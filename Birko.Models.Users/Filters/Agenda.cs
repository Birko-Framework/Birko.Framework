using System;
using System.Linq.Expressions;
using Birko.Data.Filters;

namespace Birko.Models.Users.Filters
{
    public class Agenda : IFilter<Models.Users.Agenda>
    {
        public string? Name { get; set; }
        public bool? Default { get; set; }
        public bool? IsActive { get; set; }

        public Expression<Func<Models.Users.Agenda, bool>>? Filter()
        {
            Expression<Func<Models.Users.Agenda, bool>>? result = null;

            if (!string.IsNullOrEmpty(Name))
            {
                result = Combine(result, x => x.Name == Name);
            }

            if (Default.HasValue)
            {
                var def = Default.Value;
                result = Combine(result, x => x.Default == def);
            }

            if (IsActive.HasValue)
            {
                var active = IsActive.Value;
                result = Combine(result, x => x.IsActive == active);
            }

            return result;
        }

        private static Expression<Func<Models.Users.Agenda, bool>> Combine(
            Expression<Func<Models.Users.Agenda, bool>>? left,
            Expression<Func<Models.Users.Agenda, bool>> right)
        {
            if (left == null) return right;
            var param = left.Parameters[0];
            var body = Expression.AndAlso(
                left.Body,
                Expression.Invoke(right, param));
            return Expression.Lambda<Func<Models.Users.Agenda, bool>>(body, param);
        }
    }
}
