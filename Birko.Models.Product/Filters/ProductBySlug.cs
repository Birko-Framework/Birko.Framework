using Birko.Data.Filters;
using Birko.Models;
using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Text;

namespace Birko.Filters
{
    public class ProductBySlug<T> : IRepositoryFilter<T> where T : Product
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
