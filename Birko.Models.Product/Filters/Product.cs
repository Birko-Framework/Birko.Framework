using Birko.Data.Filters;
using Birko.Models.Product;
using System;

namespace Birko.Models.Product.Filters
{
    public class Product<T> : ModelByGuid<T> where T : Product
    {
        public Product(Guid id) : base(id)
        {
        }
    }
}
