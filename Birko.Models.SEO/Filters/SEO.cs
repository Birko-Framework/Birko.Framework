using Birko.Data.Filters;
using Birko.Models.SEO;
using System;

namespace Birko.Models.SEO.Filters
{
    public class SEO<T> : ModelByGuid<T> where T : SEO
    {
        public SEO(Guid id) : base(id)
        {
        }
    }
}