using Birko.Data.Filters;
using Birko.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace Birko.Filters
{
    public class Product<T> : ModelByGuid<T> where T: Product
    {
        public Product(Guid id) : base(id)
        {
        }
    }
}
