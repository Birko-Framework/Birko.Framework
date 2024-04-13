using Birko.Data.Filters;
using Birko.Models;
using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Text;

namespace Birko.Filters
{
    public class ProductList<T> : IRepositoryFilter<T> where T : Product
    {
        public string Search { get; private set; }
        public string Category { get; private set; }
        public Dictionary<string, List<string>> Parameters { get; private set; }
        public Dictionary<string, List<string>> Tags { get; private set; }
        public decimal? PriceFrom { get; private set; }
        public decimal? PriceTo { get; private set; }

        public ProductList(string search = null, string category = null, Dictionary<string, List<string>> parameters = null, Dictionary<string, List<string>> tags = null, decimal? priceFrom = null, decimal? priceTo = null)
        {
            Search = search;
            Category = category;
            Parameters = parameters;
            Tags = tags;
            PriceFrom = priceFrom;
            PriceTo = priceTo;
        }

        public Expression<Func<T, bool>> Filter()
        {
            throw new NotImplementedException();
        }
    }
}
