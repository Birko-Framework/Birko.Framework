using Birko.Data.Filters;
using Birko.Models;
using System;
using System.Linq.Expressions;

namespace Birko.Filters
{
    public class SEOByPath<T> : IRepositoryFilter<T> where T : SEO
    {
        public string Path { get; private set; }
        public bool Exact { get; private set; } = false;
        public SEOByPath(string path, bool exact = false)
        {
            Path = path;
            Exact = exact;
        }

        public Expression<Func<T, bool>> Filter()
        {
            if(string.IsNullOrEmpty(Path))
            {
                return null;
            }
            return Exact
                ? (x) => x.Path == Path
                : (x) => x.Path.StartsWith(Path);
        }
    }
}