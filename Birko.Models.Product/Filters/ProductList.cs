using Birko.Data.Filters;
using Birko.Models.Product;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;

namespace Birko.Models.Product.Filters
{
    public class ProductList<T> : IFilter<T> where T : Product
    {
        public string? Search { get; private set; }
        public string? Category { get; private set; }
        public Dictionary<string, List<string>>? Parameters { get; private set; }
        public Dictionary<string, List<string>>? Tags { get; private set; }

        public ProductList(string? search = null, string? category = null, Dictionary<string, List<string>>? parameters = null, Dictionary<string, List<string>>? tags = null)
        {
            Search = search;
            Category = category;
            Parameters = parameters;
            Tags = tags;
        }

        public virtual Expression<Func<T, bool>>? Filter()
        {
            Expression<Func<T, bool>>? result = SearchExpression(Search);
            if (!string.IsNullOrEmpty(Category))
            {
                Expression<Func<T, bool>> right = (x) => x.Category.StartsWith(Category);
                result = (result != null) ? AndAlso(result, right) : right;
            }

            if (typeof(IProductTags).IsAssignableFrom(typeof(T)) && (Tags?.Any() ?? false))
            {
                foreach (var kvp in Tags)
                {
                    Expression<Func<T, bool>> right = (x) => ((IProductTags)x).Tags.Any(p => p.Source == kvp.Key && kvp.Value.Contains(p.Value));
                    result = (result != null) ? AndAlso(result, right) : right;
                }
            }
            if (typeof(IProductProperties).IsAssignableFrom(typeof(T)) && (Parameters?.Any()?? false))
            {
                foreach (var kvp in Parameters)
                {
                    Expression<Func<T, bool>> right = (x) => ((IProductProperties)x).Properties.Any(p => p.Source == kvp.Key && kvp.Value.Contains(p.Value));
                    result = (result != null) ? AndAlso(result, right) : right;
                }
            }

            return result;
        }

        /// <summary>
        /// Combines two single-parameter predicates with AndAlso. Each lambda literal compiles to its
        /// OWN ParameterExpression, so the right body must be rebound onto the left's parameter before
        /// merging — otherwise the combined lambda references an unbound parameter and throws
        /// InvalidOperationException ("variable 'x' … referenced from scope … not defined") on
        /// Compile() / LINQ-provider translation the moment two clauses are combined.
        /// </summary>
        private static Expression<Func<T, bool>> AndAlso(Expression<Func<T, bool>> left, Expression<Func<T, bool>> right)
        {
            var parameter = left.Parameters[0];
            var reboundRight = new ReplaceParameterVisitor(right.Parameters[0], parameter).Visit(right.Body);
            return Expression.Lambda<Func<T, bool>>(Expression.AndAlso(left.Body, reboundRight!), parameter);
        }

        private sealed class ReplaceParameterVisitor : ExpressionVisitor
        {
            private readonly ParameterExpression _from;
            private readonly Expression _to;

            public ReplaceParameterVisitor(ParameterExpression from, Expression to)
            {
                _from = from;
                _to = to;
            }

            protected override Expression VisitParameter(ParameterExpression node)
                => node == _from ? _to : base.VisitParameter(node);
        }

        protected virtual Expression<Func<T, bool>>? SearchExpression(string? filter)
        {
            return
                !string.IsNullOrEmpty(filter)
                ? (x) => x.Name.Contains(filter) /*|| x.Description.Contains(filter)*/
                : null;
        }
    }
}
