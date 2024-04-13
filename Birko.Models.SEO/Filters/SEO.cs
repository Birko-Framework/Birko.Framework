using Birko.Data.Filters;
using Birko.Models;
using System;

namespace Birko.Filters
{
    public class SEO<T> : ModelByGuid<T> where T : SEO
    {
        public SEO(Guid id) : base(id)
        {
        }
    }
}