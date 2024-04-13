using Birko.Data.Filters;
using Birko.Models;
using System;

namespace Birko.Filters
{
    public class Category<T> : ModelByGuid<T> where T : Category
    {
        public Category(Guid id) : base(id)
        {
        }
    }
}