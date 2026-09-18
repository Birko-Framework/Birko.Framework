using Birko.Data.Filters;
using Birko.Models.Product;
using System;
using System.Linq.Expressions;

namespace Birko.Models.Product.Filters
{
    public class ProductBySlug<T> : IFilter<T> where T : Product
    {
        public string Slug { get; private set; }

        public ProductBySlug(string slug)
        {
            Slug = slug;
        }

        public Expression<Func<T, bool>> Filter()
        {
            return (x) => x.Slug == Slug;
        }
    }
}
