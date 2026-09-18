using Birko.Data.Filters;
using Birko.Models.SEO;
using System;
using System.Linq.Expressions;

namespace Birko.Models.SEO.Filters
{
    public class SEOByPath<T> : IFilter<T> where T : SEO
    {
        public string Path { get; private set; }
        public bool Exact { get; private set; } = false;
        public SEOByPath(string path, bool exact = false)
        {
            Path = path;
            Exact = exact;
        }

        public Expression<Func<T, bool>>? Filter()
        {
            if(string.IsNullOrEmpty(Path))
            {
                return null;
            }
            // CR-L319: SEO.Path is declared null!, so a default/partially-loaded entity can have a null
            // Path. Guard the member access so the predicate can't NRE under LINQ-to-objects (InMemory/
            // JSON/XML backends) — SQL/NoSQL providers translate it, but in-memory evaluation would throw.
            return Exact
                ? (x) => x.Path == Path
                : (x) => x.Path != null && x.Path.StartsWith(Path);
        }
    }
}