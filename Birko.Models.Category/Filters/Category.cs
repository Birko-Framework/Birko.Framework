using Birko.Data.Filters;
using Birko.Models.Category;
using System;

namespace Birko.Models.Category.Filters
{
    public class Category<T> : ModelByGuid<T> where T : Category
    {
        public Category(Guid id) : base(id)
        {
        }
    }
}