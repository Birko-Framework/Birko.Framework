using Birko.Data.Filters;
using Birko.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;

namespace Birko.Filters
{
    public class ProductList<T> : IRepositoryFilter<T> where T : Product
    {
        public string Search { get; private set; }
        public string Category { get; private set; }
        public Dictionary<string, List<string>> Parameters { get; private set; }
        public Dictionary<string, List<string>> Tags { get; private set; }

        public ProductList(string search = null, string category = null, Dictionary<string, List<string>> parameters = null, Dictionary<string, List<string>> tags = null)
        {
            Search = search;
            Category = category;
            Parameters = parameters;
            Tags = tags;
        }

        public virtual Expression<Func<T, bool>> Filter()
        {
            Expression<Func<T, bool>> result = SearchExpression(Search);
            if (typeof(IProductTags).IsAssignableFrom(typeof(T)) && (Tags?.Any() ?? false))
            {
                foreach (var kvp in Tags)
                {
                    Expression<Func<T, bool>> right = (x) => (x as IProductTags).Tags.Any(p => p.Source == kvp.Key && kvp.Value.Contains(p.Value));
                    result = (result != null)
                        ? Expression.Lambda<Func<T, bool>>(Expression.AndAlso(result.Body, right.Body), result.Parameters.Concat(right.Parameters).Distinct())
                        : right;
                }
            }
            if (typeof(IProductProperties).IsAssignableFrom(typeof(T)) && (Parameters?.Any()?? false))
            {
                foreach (var kvp in Parameters)
                {
                    Expression<Func<T, bool>> right = (x) => (x as IProductProperties).Properties.Any(p => p.Source == kvp.Key && kvp.Value.Contains(p.Value));
                    result = (result != null)
                        ? Expression.Lambda<Func<T, bool>>(Expression.AndAlso(result.Body, right.Body), result.Parameters.Concat(right.Parameters).Distinct())
                        : right;
                }
            }

            return result;
        }

        protected virtual Expression<Func<T, bool>> SearchExpression(string filter)
        {
            return
                !string.IsNullOrEmpty(filter)
                ? (x) => x.Name.Contains(filter) /*|| x.Description.Contains(filter)*/
                : null;
        }
    }
}
